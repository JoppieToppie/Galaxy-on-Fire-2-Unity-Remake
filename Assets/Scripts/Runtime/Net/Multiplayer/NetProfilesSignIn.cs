// NetProfilesSignIn.cs
// NetProfiles: signing in and uploads (part of the partial NetProfiles; its fields are in NetProfiles.cs).

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
    public static partial class NetProfiles
    {
        // ---- signing in -------------------------------------------------------------------------------------
        /// <summary>NetState.LoginRpc: a connected game says who it is. Answers with its profile (or a fresh start), its new
        /// token if it got one, and its role.</summary>
        public static void OnLogin(ulong client, string token, string device, string name)
        {
            if (!Enabled || NetState.Instance == null || logins.ContainsKey(client)) return;
            if (NetPlayerStations.ProfileWritesBlocked && !IsHostClient(client))
            {
                NetGame.Kick(client, "Station transaction recovery is pending. Reconnect once recovery completes.");
                return;
            }
            device = CleanDevice(device);
            name = NetGame.Clean(name);
            Account account = null;
            string newToken = null;
            if (!string.IsNullOrEmpty(token))
            {
                string hash = Hash(token);
                account = index.accounts.Find(a => a.devices.Exists(d => d.tokenHash == hash));
                var entry = account?.devices.Find(d => d.tokenHash == hash);
                if (entry != null) entry.device = device;   // a label only (the token proves it)
            }
            // A ban on this profile or this device (NetModeration): dropped with the reason (never the host itself).
            if (!IsHostClient(client) && NetModeration.BanReason(account?.id, device) is string banned)
            {
                Debug.Log($"Server: client {client} ({(account != null ? "profile " + account.id : "no profile")}) is banned; dropped.");
                NetGame.Kick(client, banned);
                return;
            }
            if (!IsHostClient(client) && ReconnectRefusal(account != null ? account.id : "device " + device, name) is string tooFast)
            {
                NetGame.Kick(client, tooFast);
                return;
            }
            if (account == null)
            {
                account = new Account { id = NewAccountId(), created = Now() };
                newToken = AddDevice(account, device);
                index.accounts.Add(account);
                Debug.Log($"Server: new profile {account.id} for {(name.Length > 0 ? name : "client " + client)} ({index.accounts.Count} in all).");
                if (name.Length > 0)
                    NetNews.Post(NetNews.Kind.Pilot, $"New pilot in the sector: {NetNews.Safe(NetGame.Clean(name))} registers at Dis", -1, "newpilot", 120f);
            }
            if (!NetFactions.ReplayPendingReputation(account.id) || !ReplayPendingDeath(account.id))
            {
                NetGame.Kick(client, "Your reputation or death recovery transaction is pending. Reconnect after the server recovers it.");
                return;
            }
            // One connection per device: the older one goes (a reconnect while the dropped one still times out).
            if (account != null)
                foreach (var other in new List<Login>(logins.Values))
                    if (other.account == account && other.device == device)
                    {
                        var departing = NetSquad.Find(other.client);
                        if (other.controller && departing != null &&
                            (departing.HasRecordedDeath || departing.InSpace && departing.Hull == 0f) &&
                            NetState.Instance?.EnsureDeathPenalty(departing, refresh: false) != true)
                        {
                            NetGame.Kick(client, "Your previous connection has an unresolved death. Reconnect after the server settles it.");
                            return;
                        }
                        logins.Remove(other.client);
                        if (other.controller) Debug.Log($"Server: profile {account.id} signed in again from the same device; the older connection goes.");
                        NetGame.Kick(other.client, Localization.Extra("mpSignedInElsewhere", "You signed in again from this device."));
                    }
            var login = new Login { client = client, account = account, device = device, acceptedAt = Time.realtimeSinceStartup };
            login.controller = account == null || ControllerOf(account) == null;
            logins[client] = login;
            if (account != null)
            {
                if (name.Length > 0) account.name = name;
                account.lastSeen = Now();
                if (IsHostClient(client) && account.role < NetModeration.Master)
                {
                    account.role = NetModeration.Master;   // the world's owner
                    Debug.Log($"Server: the host ({account.name}) is this world's master admin.");
                }
                SaveIndex();
            }
            SetObserverFlag(client, !login.controller || NeedsIdentity(account));
            SendProfile(login, newToken);
            if (account != null && !login.controller)
                NetState.Instance.Notify(client, Localization.Extra("mpObserverJoined",
                    "Your profile is in use on another device: this one watches from the station. Type /control once the other one is docked."));
            if (login.controller && account != null) RestoreSquad(login);
            NetFactions.OnLogin(client);   // the faction's tag on the name
        }
        /// <summary>NetGame: a player left. A controller's profile goes to its next device online, if any.</summary>
        public static void OnDisconnect(ulong client)
        {
            incoming.Remove(client);
            if (!logins.TryGetValue(client, out var login)) return;
            logins.Remove(client);
            if (login.account == null) return;
            login.account.lastSeen = Now();
            SaveIndex();
            foreach (var pair in new List<KeyValuePair<string, (ulong from, ulong to, float until)>>(handovers))
                if (pair.Value.from == client || pair.Value.to == client)
                {
                    handovers.Remove(pair.Key);
                    if (pair.Value.from == client && logins.TryGetValue(pair.Value.to, out var waiting)) Promote(waiting);
                }
            if (login.controller && ControllerOf(login.account) == null)
                foreach (var other in logins.Values)
                    if (other.account == login.account) { Promote(other); break; }
        }
        /// <summary>NetState.Update (server): link codes and handovers that ran out.</summary>
        public static void Tick()
        {
            if (!Enabled) return;
            float now = Time.realtimeSinceStartup;
            if (!NetPlayerStations.ProfileWritesBlocked)
                foreach (var login in logins.Values)
                    if (login.economyRevisionDirty && NetState.Instance != null && NetState.Instance.IsSpawned)
                    {
                        login.economyRevisionDirty = false;
                        NetState.Instance.SendEconomyRevision(login.client, login.economyRevision);
                    }
            // A disconnect during journal recovery may have deferred controller promotion.
            if (!NetPlayerStations.ProfileWritesBlocked)
                foreach (var login in new List<Login>(logins.Values))
                    if (login.account != null && !login.controller && ControllerOf(login.account) == null &&
                        !handovers.ContainsKey(login.account.id)) Promote(login);
            TickDeathRecoveries();
            if (now >= nextPrune) Prune();   // hourly: profiles unused for PruneDays
            if (links.Count > 0)
                foreach (var code in new List<string>(links.Keys))
                    if (links[code].until < now) links.Remove(code);
            if (handovers.Count > 0)
                foreach (var pair in new List<KeyValuePair<string, (ulong from, ulong to, float until)>>(handovers))
                {
                    if (pair.Value.until >= now) continue;
                    handovers.Remove(pair.Key);   // no upload in time: the last saved profile
                    if (logins.TryGetValue(pair.Value.to, out var to)) Promote(to);
                }
        }
        static Login ControllerOf(Account account)
        {
            foreach (var l in logins.Values) if (l.account == account && l.controller) return l;
            return null;
        }
        /// <summary>This device controls its profile now: it gets the latest saved profile (its game reloads the station).</summary>
        static void Promote(Login login)
        {
            if (login.account != null && ControllerOf(login.account) != null) return;
            if (NetPlayerStations.ProfileWritesBlocked || (login.account != null && (!NetFactions.ReplayPendingReputation(login.account.id) || !ReplayPendingDeath(login.account.id)))) return;
            login.controller = true;
            login.acceptedUploadSequence = 0;
            incoming.Remove(login.client);
            login.acceptedAt = Time.realtimeSinceStartup;
            SetObserverFlag(login.client, NeedsIdentity(login.account));
            SendProfile(login, null);
            NetState.Instance?.Notify(login.client, Localization.Extra("mpNowController", "This device controls your profile now."));
            Debug.Log($"Server: client {login.client} controls profile {login.account?.id} now.");
        }
        static void Demote(Login login)
        {
            login.controller = false;
            SetObserverFlag(login.client, true);
            NetState.Instance?.SendRole(login.client, false);
        }
        static void SetObserverFlag(ulong client, bool on)
        {
            var p = NetSquad.Find(client);
            if (p != null) p.SetObserver(on);
        }
        static void SendProfile(Login login, string newToken)
        {
            string json = "";
            // The co-op campaign's host plays its own save slot (or the new game it made, ""), never a profile here.
            bool coopHost = NetGame.CoopSession && IsHostClient(login.client);
            if (coopHost) json = NetCoop.HostSave ?? "";
            else if (login.account != null)
                try { if (File.Exists(ProfilePath(login.account.id))) json = File.ReadAllText(ProfilePath(login.account.id)); }
                catch (Exception e) { Debug.LogError($"NetProfiles: profile {login.account.id} unreadable: {e.Message}"); }
            // A profile saved before the upgrade cap: cut back here too, so the game gets (and next uploads) the capped hull.
            SaveData saved = null;
            bool parsed = !coopHost && json.Length > 0 && SaveGame.TryParse(json, NetGame.Db, out saved, out _);
            // A missing or unreadable established profile must not be replaced by a fresh
            // client snapshot. Preserve both disk copies for recovery on advanced economy servers.
            if (!coopHost && login.account != null && (Modding.ModAdvancedEconomy.DynamicPricing || Flight.Standing.Independent) &&
                !parsed && (File.Exists(ProfilePath(login.account.id)) || File.Exists(ProfilePath(login.account.id) + ".bak")))
            {
                NetGame.Kick(login.client, "Your saved profile could not be loaded. Contact the server administrator to recover it.");
                return;
            }
            int cut = parsed ? ClampMods(saved) : 0;
            int misfits = saved != null ? ClampClasses(saved) : 0;   // remake mods: gear the ship's class can't mount
            if (cut > 0 || misfits > 0)
            {
                json = JsonUtility.ToJson(saved, false);
                if (Write(ProfilePath(login.account.id), json))
                {
                    if (cut > 0) Debug.LogWarning($"Server: profile {login.account.id} ({login.account.name}): {cut} Kaamo upgrade level(s) past {Session.MaxModLevel} per mod removed.");
                    if (misfits > 0) Debug.LogWarning($"Server: profile {login.account.id} ({login.account.name}): {misfits} item(s) the ship's class can't mount moved to the hold.");
                }
            }
            // Replacing a remote client's snapshot is itself an economy barrier. An upload
            // assembled before a refresh or handover cannot be used for a later trade.
            if (Modding.ModAdvancedEconomy.DynamicPricing && !IsHostClient(login.client))
            {
                login.economyRevision++;
                login.economyRevisionDirty = true;
                login.acceptedUploadSequence = 0;
                incoming.Remove(login.client);
            }
            RefreshServerReputation(NetSquad.Find(login.client));
            int home = login.account != null ? NetFactions.HomeOf(login.account.id) : -1;   // a faction member starts at its home
            NetState.Instance?.SendProfile(login.client, newToken, login.controller, login.account == null, json, home,
                NeedsIdentity(login.account),
                NeedsIdentity(login.account) && !PlayerIdentity.TryResolveSelectableRace(login.account?.stationPolicyRace, out _) ? null : login.account?.stationPolicyRace,
                Modding.ModPlayerInfrastructure.Current.PlayerIdentityEnabled ? login.account?.playerPortrait : null);
            if (!NetPlayerStations.ProfileWritesBlocked && !IsHostClient(login.client))
            {
                login.economyRevisionDirty = false;
                NetState.Instance?.SendEconomyRevision(login.client, login.economyRevision);
            }
            NetModeration.SyncRole(login.client);   // the profile's role onto its NetPlayer (NetCommands' rights)
        }
        /// <summary>The log's note on a signed-in client: its profile and device label ("" when it has none).</summary>
        public static string Describe(ulong client)
        {
            if (!logins.TryGetValue(client, out var l)) return "";
            string dev = string.IsNullOrEmpty(l.device) ? "?" : l.device.Length > 8 ? l.device.Substring(0, 8) : l.device;
            return l.account != null ? $"profile {l.account.id}, device {dev}{(l.controller ? "" : ", observer")}" : $"no profile, device {dev}";
        }
        /// <summary>OnLogin: why this sign-in is turned away for reconnecting too often (null = it may sign in). 'key' is the
        /// profile, or the device for a game without one.</summary>
        static string ReconnectRefusal(string key, string name)
        {
            float now = Time.realtimeSinceStartup;
            if (reconnectBlocked.TryGetValue(key, out float until))
            {
                if (now < until) return ReconnectText(until - now);
                reconnectBlocked.Remove(key);
            }
            if (!recentLogins.TryGetValue(key, out var times)) recentLogins[key] = times = new List<float>();
            times.RemoveAll(t => now - t > ReconnectWindow);
            times.Add(now);
            if (times.Count <= ReconnectMax) return null;
            times.Clear();
            reconnectBlocked[key] = now + ReconnectBlockSeconds;
            Debug.LogWarning($"Server: {key} ({(name.Length > 0 ? name : "no name")}) signed in more than {ReconnectMax} times in " +
                             $"{ReconnectWindow / 60f:0} minutes; turned away for {ReconnectBlockSeconds / 60f:0} minutes.");
            return ReconnectText(ReconnectBlockSeconds);
        }
        static string ReconnectText(float seconds) => string.Format(Localization.Extra("mpReconnectTooFast",
            "You reconnected too often. Try again in {0} minutes."), Mathf.Max(1, Mathf.CeilToInt(seconds / 60f)));
        /// <summary>Kaamo upgrades past Session.MaxModLevel of one mod on one hull (the flown ship, the Kaamo Club's and the
        /// parked one) cut back to it. Returns how many levels went.</summary>
        static int ClampMods(SaveData s)
        {
            int Clamp(List<int> mods)
            {
                if (mods == null || mods.Count == 0) return 0;
                var levels = new Dictionary<int, int>();
                int before = mods.Count;
                mods.RemoveAll(m =>
                {
                    levels.TryGetValue(m, out int n);
                    levels[m] = n + 1;
                    return n >= Session.MaxModLevel;
                });
                return before - mods.Count;
            }
            int cut = Clamp(s.shipMods);
            if (s.kaamoShips != null) foreach (var k in s.kaamoShips) if (k != null) cut += Clamp(k.mods);
            if (s.parkedShip != null) cut += Clamp(s.parkedShip.mods);
            return cut;
        }
        /// <summary>Remake mods (Modding.ShipClassRules): what the flown ship and the parked one carry that their class can't
        /// mount (items.json allowedClasses / excludedClasses) goes to the same ship's hold: the client enforces it in the
        /// hangar (Hangar.CanMount), this is the server's say on what an upload claims. Original items and ships without a class
        /// never move. A Kaamo Club hull's gear is checked when it is taken out (Hangar.SwitchTo). Returns how many stacks moved.</summary>
        static int ClampClasses(SaveData s)
        {
            var db = NetGame.Db;
            if (s == null || db == null) return 0;
            int Clamp(int ship, List<ItemStack> equipment, List<ItemStack> cargo)
            {
                if (equipment == null || cargo == null) return 0;
                var hull = db.Ship(ship);
                int moved = 0;
                for (int i = equipment.Count - 1; i >= 0; i--)
                {
                    var e = equipment[i];
                    if (e == null || e.builtIn || Modding.ShipClassRules.Fits(db.Item(e.item), hull)) continue;
                    equipment.RemoveAt(i);
                    int units = Mathf.Max(1, e.amount);
                    var held = cargo.Find(c => c != null && c.item == e.item);
                    if (held != null) held.amount += units; else cargo.Add(new ItemStack(e.item, units));
                    moved++;
                }
                return moved;
            }
            int total = Clamp(s.ship, s.equipment, s.cargo);
            if (s.hasParkedShip && s.parkedShip != null) total += Clamp(s.parkedShip.ship, s.parkedShip.equipment, s.parkedShip.cargo);
            return total;
        }
        // ---- uploads ----------------------------------------------------------------------------------------
        /// <summary>NetState.UploadChunkRpc: a piece of a client's profile; the last one checks and saves it.</summary>
        public static void OnUploadChunk(ulong client, int seq, int part, int count, long economyRevision, byte[] data)
        {
            if (!Enabled || seq <= 0 || data == null || data.Length > ChunkBytes || count < 1 || count > MaxChunks || part < 0 || part >= count) return;
            if (!logins.TryGetValue(client, out var login) || login.account == null ||
                seq <= login.lastAcceptedUploadSequence || economyRevision != login.economyRevision || login.economyRevisionDirty || NetPlayerStations.ProfileWritesBlocked) return;
            if (incoming.TryGetValue(client, out var prior) && seq < prior.seq) return;
            if (!incoming.TryGetValue(client, out var inc) || inc.seq != seq)
                incoming[client] = inc = new Incoming { seq = seq, count = count, economyRevision = economyRevision, parts = new byte[count][] };
            if (inc.count != count || inc.economyRevision != economyRevision || inc.parts[part] != null) return;
            inc.parts[part] = data;
            if (++inc.got < count) return;
            incoming.Remove(client);
            string json = Unpack(inc.parts);
            if (json != null) Receive(client, json, seq, inc.economyRevision);
        }
        static void Receive(ulong client, string json, int acceptedSequence = 0, long uploadRevision = 0)
        {
            if (!logins.TryGetValue(client, out var login) || login.account == null) return;
            var account = login.account;
            bool handing = handovers.TryGetValue(account.id, out var handover) && handover.from == client;
            if (NeedsIdentity(account) || (!login.controller && !handing)) return;   // incomplete profiles and observers don't write
            if (NetPlayerStations.ProfileWritesBlocked)
            {
                Reject(login, "a station transaction is awaiting durable recovery; retry after recovery completes");
                return;
            }
            if (HasPendingDeath(account.id) || NetFactions.HasPendingReputation(account.id))
            {
                Reject(login, "a death or reputation transaction is pending");
                return;
            }
            if (acceptedSequence != 0 && acceptedSequence <= login.lastAcceptedUploadSequence) return;
            float now = Time.realtimeSinceStartup;
            if (!SaveGame.TryParse(json, NetGame.Db, out var save, out string problem))
            {
                Reject(login, problem);
                return;
            }
            if (!IsHostClient(client) && Modding.ModAdvancedEconomy.DynamicPricing && save.cargo != null)
            {
                var seen = new HashSet<int>();
                foreach (var stack in save.cargo)
                    if (stack != null && (stack.builtIn || !seen.Add(stack.item)))
                    {
                        Reject(login, "your cargo contains duplicate or integrated item rows");
                        return;
                    }
            }
            // Settle the server-observed death before reading the canonical profile.
            // Settlement can replace the profile and invalidate the upload revision.
            var pilot = NetSquad.Find(client);
            if (pilot != null && pilot.HasRecordedDeath && pilot.RecordedDeathRequiresPenalty &&
                NetState.Instance?.EnsureDeathPenalty(pilot, refresh: false) != true)
            {
                Reject(login, "your recorded death has not been settled");
                return;
            }
            if (acceptedSequence != 0 && (uploadRevision != login.economyRevision || login.economyRevisionDirty || NetPlayerStations.ProfileWritesBlocked))
            {
                Reject(login, "your profile changed while the upload was being validated");
                return;
            }
            // A stale upload must not resurrect the pre-death loadout, even if it was
            // assembled after receipt of the new economy revision.
            var prior = ReadSave(account.id);
            if (!IsHostClient(client) && (Modding.ModAdvancedEconomy.DynamicPricing || Flight.Standing.Independent) && prior == null &&
                (File.Exists(ProfilePath(account.id)) || File.Exists(ProfilePath(account.id) + ".bak")))
            {
                Reject(login, "your established server profile is unavailable; administrator recovery is required");
                return;
            }
            if (!string.IsNullOrEmpty(prior?.lastDeathPenaltyId) &&
                !string.Equals(prior.lastDeathPenaltyId, save.lastDeathPenaltyId, StringComparison.Ordinal))
            {
                Reject(login, "your profile has not acknowledged the latest death recovery");
                if (!IsHostClient(client)) RefreshProfile(client);
                return;
            }
            if (Flight.Standing.Independent)
            {
                if (!IsHostClient(client) && !string.IsNullOrEmpty(prior?.reputationRevision) &&
                    !string.Equals(prior.reputationRevision, save.reputationRevision, StringComparison.Ordinal))
                {
                    Reject(login, "your profile has not acknowledged the latest reputation change");
                    if (pilot != null && pilot.Where == NetPlayer.Place.Hangar) RefreshProfile(client);
                    else PublishReputation(pilot, prior, true);
                    return;
                }
                if (!IsHostClient(client))
                    PreserveServerReputation(save, prior, NetFactions.HasPiracyHistory(account.id), NetFactions.IsPirateAccount(account.id));
                if (SavedReputation(prior).ContainsKey(Flight.Standing.PiracyHistoryKey) || NetFactions.HasPiracyHistory(account.id))
                    SetSavedReputation(save, Flight.Standing.PiracyHistoryKey, 1);
                if (NetFactions.IsPirateAccount(account.id))
                {
                    SetSavedReputation(save, Flight.Standing.PiracyHistoryKey, 1);
                    foreach (int race in Flight.Standing.ReputationRaces())
                        SetSavedReputation(save, Flight.Standing.ReputationKey(race), -100);
                }
                if (!IsHostClient(client)) save.reputationRevision = prior?.reputationRevision;
            }
            // Reward receipts are server-owned. Refuse an upload that predates a payout even if
            // the client has already received the newer economy revision.
            if (prior?.factionWarRewardReceipts != null &&
                prior.factionWarRewardReceipts.Exists(id => save.factionWarRewardReceipts?.Contains(id) != true))
            {
                Reject(login, "your profile has not acknowledged the latest faction war reward");
                if (!IsHostClient(client)) RefreshProfile(client);
                return;
            }
            save.factionWarRewardReceipts = prior?.factionWarRewardReceipts == null
                ? new List<string>() : new List<string>(prior.factionWarRewardReceipts);
            // The origin grant is a creation-only server intent, never accepted from an upload.
            save.originStartAsPirate = false;
            // Recovery records belong to the server, not to the uploading client.
            save.deathRecoveries = prior?.deathRecoveries ?? new List<DeathRecovery>();
            save.lastDeathPenaltyId = prior?.lastDeathPenaltyId;
            if (prior?.deathLoaner == true && save.ship == prior.ship && !IsHostClient(client))
            {
                int before = prior.kaamoShips?.FindAll(x => x != null && x.ship == save.ship).Count ?? 0;
                int after = save.kaamoShips?.FindAll(x => x != null && x.ship == save.ship).Count ?? 0;
                if (after >= before) save.deathLoaner = true;
            }
            int cut = ClampMods(save);
            int misfits = ClampClasses(save);   // remake mods: gear the ship's class can't mount
            json = JsonUtility.ToJson(save, false);
            if (cut > 0 || misfits > 0)
            {
                json = JsonUtility.ToJson(save, false);
                if (cut > 0) Debug.LogWarning($"Server: profile {account.id} ({account.name}): {cut} Kaamo upgrade level(s) past {Session.MaxModLevel} per mod removed.");
                if (misfits > 0) Debug.LogWarning($"Server: profile {account.id} ({account.name}): {misfits} item(s) the ship's class can't mount moved to the hold.");
            }
            long worth = Worth(save);
            string why = !NetGame.HostAllowsDebug && !IsHostClient(client) ? Implausible(login, save, worth) : null;
            // Audit every accepted upload for provenance, not merely large credit-value changes.
            // Refused uploads must not consume receipts; failed disk writes are not accepted.
            float minutes = Mathf.Max(0f, now - login.acceptedAt) / 60f;
            if (why != null)
            {
                NetAudit.Upload(account.id, account.name, client, prior, save, account.worth, minutes, why);
                Reject(login, why);
                return;
            }
            if (!Write(ProfilePath(account.id), json)) return;
            NetAudit.Upload(account.id, account.name, client, prior, save, account.worth, minutes, null);
            login.acceptedUploadSequence = acceptedSequence;
            if (acceptedSequence != 0) login.lastAcceptedUploadSequence = acceptedSequence;
            account.worth = worth;
            account.playSeconds = save.playSeconds;
            account.lastSeen = Now();
            login.acceptedAt = now;
            SaveIndex();
            if (Flight.Standing.Independent && !IsHostClient(client)) PublishReputation(pilot, save, true);
            if (!handing) NetState.Instance?.ClaimVictoryReward(pilot, save);
            if (handing)
            {
                handovers.Remove(account.id);
                if (logins.TryGetValue(handover.to, out var to)) Promote(to);
            }
        }
        /// <summary>A persistent hosted world: the host's own game saved straight into its profile (NetGame.Shutdown, so the
        /// last minutes aren't lost when the host closes the session).</summary>
        internal static void SaveHost(string json)
        {
            if (Enabled && !string.IsNullOrEmpty(json)) Receive(Unity.Netcode.NetworkManager.ServerClientId, json);
        }
        static void Reject(Login login, string why)
        {
            NetState.Instance?.Notify(login.client, string.Format(Localization.Extra("mpNotSaved", "The server didn't save your progress: {0}."), why));
            // The log and the staff: the first refusal, then one line per RejectLogSeconds with the count (an upload a
            // minute filled the log with one line each).
            float now = Time.realtimeSinceStartup;
            string id = login.account.id;
            rejectLog.TryGetValue(id, out var last);
            if (last.at > 0f && now - last.at < RejectLogSeconds) { rejectLog[id] = (last.at, last.count + 1); return; }
            string since = last.count > 0 ? $" ({last.count} more refused since the last report)" : "";
            rejectLog[id] = (now, 0);
            Debug.LogWarning($"Server: profile {id} ({login.account.name}, client {login.client}): upload not saved: {why}.{since}");
            string staff = $"[server] {login.account.name}'s upload was not saved: {why}.";
            foreach (var other in logins.Values)
                if (other.account != null && other.account.role >= NetModeration.Op && other.client != login.client)
                    NetState.Instance?.Notify(other.client, staff);
        }
        /// <summary>Without the Debug menu: what can't have happened in the game (null = plausible).</summary>
        static string Implausible(Login login, SaveData save, long worth)
        {
            if (save.ship == 13 || save.ship == 14 || save.ship == 15) return "a ship nobody can own";
            if (save.kaamoShips != null && save.kaamoShips.Exists(k => k.ship == 13 || k.ship == 14 || k.ship == 15)) return "a stored ship nobody can own";
            float minutes = Mathf.Max(0f, Time.realtimeSinceStartup - login.acceptedAt) / 60f;
            long allowed = EarnBurst + (long)(minutes * EarnPerMinute);
            long growth = worth - Math.Max(0L, login.account.worth);
            if (growth > allowed)
                return $"its worth grew by {growth:N0} in {minutes:0.#} minutes (at most {allowed:N0})";
            return null;
        }
        /// <summary>What a profile owns: credits, the ships (their price) and every item at its lowest price.</summary>
        static long Worth(SaveData s) => NetAudit.Of(s).Total;
        /// <summary>The profile's last accepted save on disk (NetAudit's comparison), null when none or unreadable.</summary>
        static SaveData ReadSaved(string id)
        {
            try
            {
                string path = ProfilePath(id);
                return File.Exists(path) ? JsonUtility.FromJson<SaveData>(File.ReadAllText(path)) : null;
            }
            catch (Exception) { return null; }
        }
    }
}
