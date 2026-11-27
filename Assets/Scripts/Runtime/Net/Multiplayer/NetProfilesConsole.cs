// NetProfilesConsole.cs
// NetProfiles: chat commands, squads, the station window, arena stats and the server console (part of the partial NetProfiles; its fields are in NetProfiles.cs).

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
        // ---- chat commands ----------------------------------------------------------------------------------
        /// <summary>NetState.SendChatRpc: a line starting with '/' while profiles are on. The answer goes only to its sender.</summary>
        public static string Command(ulong client, string text)
        {
            if (!logins.TryGetValue(client, out var login)) return Localization.Extra("mpNotSignedIn", "Not signed in yet.");
            var words = text.Trim().Split(new[] { ' ' }, StringSplitOptions.RemoveEmptyEntries);
            string cmd = words.Length > 0 ? words[0].ToLowerInvariant() : "";
            switch (cmd)
            {
                case "/link":
                    return words.Length == 1 ? NewLinkCode(login) : UseLinkCode(login, words[1], words.Length > 2 && words[2].ToLowerInvariant() == "force");
                case "/appearance": return SetOwnAppearance(login, words);
                case "/control":
                    return TakeControl(login);
                case "/profile": case "/whoami":
                    if (login.account == null) return Localization.Extra("mpGuestShort", "You play as a guest: nothing is saved.");
                    return string.Format(Localization.Extra("mpProfileInfo", "Profile {0}, {1} device(s); this one {2}."), login.account.id,
                        login.account.devices.Count, login.controller ? Localization.Extra("mpControls", "controls it") : Localization.Extra("mpWatches", "watches"));
                default:
                    return Localization.Extra("mpCommands", "Commands: /duel <name>, /accept, /decline, /ffa, /leave, /arena, /top; /faction (help: /faction help), /f <text>; /link (a code for another device), /link CODE, /control, /profile; /staff (ops: /kick, /tempban, /unban, /bans; admins: /ban, /op, /deop).");
            }
        }
        static string SetOwnAppearance(Login login, string[] words)
        {
            if (!NetGame.IsServer || !Modding.ModPlayerInfrastructure.Current.PlayerIdentityEnabled)
                return "Player race and portrait selection is disabled.";
            if (login.account == null || !login.controller) return "Take control of a persistent profile first.";
            if (NetPlayerStations.ProfileWritesBlocked) return "Profile changes are paused during station transaction recovery.";
            if (words.Length != 3 || !PlayerIdentity.TryResolveSelectableRace(words[1], out int race) ||
                !PlayerIdentity.TryResolveSelectionPortrait(race, words[2], out _, out _))
                return "Choose a playable race and a matching portrait. Void and Specters are not playable.";
            string raceKey = PlayerIdentity.RaceKey(race);
            string savedRace = login.account.stationPolicyRace;
            if (!PlayerIdentity.IsComplete(savedRace, login.account.playerPortrait) &&
                !PlayerIdentity.TryResolveSelectableRace(savedRace, out _)) savedRace = null;
            if (!PlayerIdentity.CanChoose(savedRace, login.account.playerPortrait, raceKey))
                return "Your pilot identity is permanent and cannot be changed.";
            // Only a genuinely new profile gets origin benefits. Persist before completing identity,
            // so a disconnect cannot reroll the origin or receive it again.
            if (!NetGame.CoopSession)
            {
                try
                {
                    string path = ProfilePath(login.account.id);
                    if (!File.Exists(path) && !File.Exists(path + ".bak") && login.account.playSeconds <= 0f)
                    {
                        var initial = PlayerIdentity.CreateOriginProfile(NetGame.Db, race, words[2]);
                        if (initial != null)
                        {
                            string json = JsonUtility.ToJson(initial, false);
                            if (!SaveGame.TryParse(json, NetGame.Db, out _, out string problem))
                                return "Race origin profile is invalid: " + problem;
                            if (!Write(path, json)) return "Could not persist your starting profile. Try again.";
                            login.account.worth = Worth(initial);
                            login.account.originCompletedMissions = initial.graphQuestsDone ?? new List<string>();
                        }
                    }
                    else if (File.Exists(path))
                    {
                        var prior = ReadSave(login.account.id);
                        if (prior != null && prior.playSeconds <= 0f &&
                            !string.IsNullOrEmpty(prior.playerIdentityRace) &&
                            !string.Equals(prior.playerIdentityRace, raceKey, StringComparison.Ordinal))
                            return "Your starting profile has already been assigned a different race.";
                        if (prior != null && prior.playSeconds <= 0f && prior.playerIdentityRace == raceKey)
                            login.account.originCompletedMissions = prior.graphQuestsDone ?? new List<string>();
                    }
                }
                catch (Exception error)
                {
                    Debug.LogWarning("Race origin: " + error.Message);
                    return "Race origin configuration could not be applied: " + error.Message;
                }
            }
            if (!SavePlayerIdentity(login.account, raceKey, words[2])) return "Pilot appearance could not be saved. Try again.";
            foreach (var device in logins.Values)
                if (device.account == login.account)
                {
                    SetObserverFlag(device.client, !device.controller);
                    SendProfile(device, null);
                }
            return "Pilot identity saved permanently.";
        }

        // These requests are authenticated by the connection and do not require a spawned pilot.
        internal static string CompleteIdentity(ulong client, string race, string portrait)
        {
            if (!logins.TryGetValue(client, out var login)) return "Not signed in yet.";
            if (string.IsNullOrEmpty(race) || string.IsNullOrEmpty(portrait) || race.Length > 128 || portrait.Length > 256)
                return "Choose a valid race and portrait.";
            return SetOwnAppearance(login, new[] { "/appearance", race, portrait });
        }

        internal static string LinkDuringIdentity(ulong client, string code)
        {
            if (!logins.TryGetValue(client, out var login) || !NeedsIdentity(login.account)) return "No pending profile setup.";
            if (NetPlayerStations.ProfileWritesBlocked) return "Profile recovery is pending.";
            if (string.IsNullOrWhiteSpace(code) || code.Length > 16) return "Enter a valid link code.";
            return UseLinkCode(login, code, false);
        }

        static string NewLinkCode(Login login)
        {
            if (login.account == null) return Localization.Extra("mpGuestNoLink", "A guest has no profile to link.");
            if (!login.controller) return Localization.Extra("mpLinkFromController", "Ask for a link code on the device that controls the profile.");
            string code;
            do
            {
                var sb = new StringBuilder();
                var bytes = new byte[6];
                using (var r = RandomNumberGenerator.Create()) r.GetBytes(bytes);
                foreach (byte b in bytes) sb.Append(LinkAlphabet[b % LinkAlphabet.Length]);
                code = sb.ToString();
            } while (links.ContainsKey(code));
            links[code] = (login.account.id, Time.realtimeSinceStartup + LinkCodeSeconds);
            return string.Format(Localization.Extra("mpLinkCode",
                "Link code {0} (5 minutes): on your other device join this server and type /link {0} in the chat."), code);
        }
        static string UseLinkCode(Login login, string code, bool force)
        {
            code = (code ?? "").Trim().ToUpperInvariant();
            if (!links.TryGetValue(code, out var link) || link.until < Time.realtimeSinceStartup)
                return Localization.Extra("mpLinkUnknown", "That link code is unknown or ran out.");
            var target = index.accounts.Find(a => a.id == link.account);
            if (target == null) { links.Remove(code); return Localization.Extra("mpLinkUnknown", "That link code is unknown or ran out."); }
            if (target == login.account) return Localization.Extra("mpLinkSame", "This device already uses that profile.");
            if (NeedsIdentity(target) && !NeedsIdentity(login.account))
                return "Complete the target profile's identity on its original device before linking it.";
            var own = login.account;
            if (own != null)
            {
                bool only = own.devices.Count <= 1;
                if (only && own.playSeconds > 300f && !force)
                    return string.Format(Localization.Extra("mpLinkForce",
                        "This device has a profile of its own with progress: linking deletes it. Type /link {0} force to go ahead."), code);
                if (login.controller) Demote(login);
                own.devices.RemoveAll(d => d.device == login.device);
                if (only) DeleteAccount(own);
            }
            links.Remove(code);
            string token = AddDevice(target, login.device);
            login.account = target;
            login.controller = ControllerOf(target) == null;
            login.acceptedAt = Time.realtimeSinceStartup;
            target.lastSeen = Now();
            SaveIndex();
            SetObserverFlag(login.client, !login.controller || NeedsIdentity(login.account));
            SendProfile(login, token);
            Debug.Log($"Server: client {login.client} linked to profile {target.id} ({target.devices.Count} devices).");
            if (login.controller) RestoreSquad(login);
            NetFactions.OnLogin(login.client);
            return login.controller ? Localization.Extra("mpLinked", "Linked: this device uses your profile now.")
                                    : Localization.Extra("mpLinkedObserver", "Linked. Your other device controls the profile: this one watches until you type /control.");
        }
        static string TakeControl(Login login)
        {
            if (login.account == null) return Localization.Extra("mpGuestShort", "You play as a guest: nothing is saved.");
            if (login.controller) return Localization.Extra("mpAlreadyController", "This device already controls your profile.");
            if (handovers.ContainsKey(login.account.id)) return Localization.Extra("mpHandoverBusy", "Taking control already...");
            var current = ControllerOf(login.account);
            if (current == null) { Promote(login); return ""; }
            var player = NetSquad.Find(current.client);
            if (player != null && !player.InHangar)
                return Localization.Extra("mpControllerFlying", "Your other device is in space: take control once it is docked.");
            Demote(current);
            handovers[login.account.id] = (current.client, login.client, Time.realtimeSinceStartup + HandoverSeconds);
            NetState.Instance?.RequestUpload(current.client);
            NetState.Instance?.Notify(current.client, Localization.Extra("mpControlTaken", "Another device of yours took control of the profile: this one only watches now."));
            return Localization.Extra("mpTakingControl", "Taking control...");
        }
        // ---- squads -----------------------------------------------------------------------------------------
        /// <summary>NetState: 'joiner' joined 'leader''s squad (both remember it).</summary>
        public static void SquadJoined(ulong leader, ulong joiner)
        {
            if (!Enabled || !logins.TryGetValue(leader, out var l) || !logins.TryGetValue(joiner, out var j) || l.account == null || j.account == null) return;
            if (string.IsNullOrEmpty(l.account.squad)) l.account.squad = RandomHex(8);
            j.account.squad = l.account.squad;
            SaveIndex();
        }
        /// <summary>NetState: the player left their squad on purpose (a disconnect keeps it).</summary>
        public static void SquadLeft(ulong client)
        {
            if (!Enabled || !logins.TryGetValue(client, out var l) || l.account == null || string.IsNullOrEmpty(l.account.squad)) return;
            l.account.squad = null;
            SaveIndex();
        }
        /// <summary>A controller signed in: back into the squad of a remembered squadmate who is online.</summary>
        static void RestoreSquad(Login login)
        {
            string key = login.account?.squad;
            if (string.IsNullOrEmpty(key) || NetState.Instance == null) return;
            var me = NetSquad.Find(login.client);
            foreach (var other in logins.Values)
            {
                if (other == login || !other.controller || other.account == null || other.account == login.account || other.account.squad != key) continue;
                var mate = NetSquad.Find(other.client);
                if (mate == null) continue;
                NetState.Instance.RestoreSquad(mate, me);
                return;
            }
        }
        // ---- the station window (NetPanel) -----------------------------------------------------------------
        /// <summary>The player's profile into the window's snapshot, and the leaderboard.</summary>
        internal static void FillPanel(ulong client, NetPanel.State s)
        {
            s.profiles = Enabled;
            if (!Enabled) return;
            if (logins.TryGetValue(client, out var l))
            {
                s.guest = l.account == null;
                s.controller = l.controller;
                if (l.account != null)
                {
                    s.profileId = l.account.id;
                    s.devices = l.account.devices.Count;
                    s.playerIdentityEnabled = Modding.ModPlayerInfrastructure.Current.PlayerIdentityEnabled;
                    s.playerRace = l.account.stationPolicyRace;
                    s.playerPortrait = l.account.playerPortrait;
                }
            }
            s.top = Leaderboard();
        }
        // ---- arena stats (NetArena) -------------------------------------------------------------------------
        /// <summary>A finished match's kills, deaths and win into the player's profile (guests: nothing).</summary>
        public static void AddArenaStats(ulong client, int kills, int deaths, bool won)
        {
            if (!Enabled || !logins.TryGetValue(client, out var l) || l.account == null) return;
            l.account.arenaKills += kills;
            l.account.arenaDeaths += deaths;
            if (won) l.account.arenaWins++;
            SaveIndex();
        }
        /// <summary>/top: the ten profiles with the most arena wins (then kills).</summary>
        public static string Leaderboard()
        {
            if (!Enabled) return Localization.Extra("mpTopNone", "This server keeps no profiles, so no leaderboard.");
            var list = index.accounts.FindAll(a => a.arenaKills + a.arenaDeaths > 0);
            if (list.Count == 0) return Localization.Extra("mpTopEmpty", "No arena matches fought yet.");
            list.Sort((a, b) => a.arenaWins != b.arenaWins ? b.arenaWins.CompareTo(a.arenaWins) : b.arenaKills.CompareTo(a.arenaKills));
            var sb = new StringBuilder(Localization.Extra("mpTopTitle", "Arena leaderboard (wins, kills / deaths):"));
            for (int i = 0; i < list.Count && i < 10; i++)
                sb.Append($"\n{i + 1}. {(string.IsNullOrEmpty(list[i].name) ? list[i].id : list[i].name)}: {list[i].arenaWins}, {list[i].arenaKills} / {list[i].arenaDeaths}");
            return sb.ToString();
        }
        // ---- the server console -----------------------------------------------------------------------------
        /// <summary>DedicatedServer's "profiles" command.</summary>
        public static string ConsoleList()
        {
            if (!Enabled) return "Player profiles are off (-noprofiles).";
            var sb = new StringBuilder($"{index.accounts.Count} profile(s) in {folder} (unused for {PruneDays} days: pruned):");
            foreach (var a in index.accounts)
            {
                string online = "";
                foreach (var l in logins.Values) if (l.account == a) online += $" client {l.client}{(l.controller ? "" : " (watching)")}";
                sb.Append($"\n  {a.id}  {(string.IsNullOrEmpty(a.name) ? "?" : a.name),-20}  {a.devices.Count} device(s), worth {a.worth:N0}, last seen {a.lastSeen}{(online.Length > 0 ? ", online:" + online : "")}");
            }
            return sb.ToString();
        }
        /// <summary>DedicatedServer's "profile delete &lt;id&gt;": not while one of its devices is online.</summary>
        /// <summary>Console "profile delete": a profile by its id or last pilot name. Online it is refused unless 'force': its
        /// devices are signed out (what they still upload is ignored) and dropped, then it goes.</summary>
        public static string ConsoleDelete(string who, bool force = false)
        {
            if (!Enabled) return "Player profiles are off (-noprofiles).";
            string id = FindAccount((who ?? "").Trim());
            var a = id != null ? index.accounts.Find(x => x.id == id) : null;
            if (a == null) return $"No profile \"{who}\" (see \"profiles\").";
            var online = new List<ulong>();
            foreach (var l in logins.Values) if (l.account == a) online.Add(l.client);
            if (online.Count > 0 && !force)
                return $"Profile {id} ({a.name}) is online (client {string.Join(", ", online)}): kick it first, or \"profile delete {id} force\".";
            foreach (ulong c in online)
            {
                logins.Remove(c);
                incoming.Remove(c);
                NetGame.Kick(c, Localization.Extra("mpProfileDeleted", "Your profile on this server was deleted."));
            }
            DeleteAccount(a);
            SaveIndex();
            return $"Deleted profile {id} ({(string.IsNullOrEmpty(a.name) ? "?" : a.name)}){(online.Count > 0 ? $", {online.Count} device(s) dropped" : "")} (its file is kept as {id}.json.bak).";
        }
        /// <summary>Console "profile info": one profile's details, ban included.</summary>
        public static string ConsoleInfo(string who)
        {
            if (!Enabled) return "Player profiles are off (-noprofiles).";
            string id = FindAccount((who ?? "").Trim());
            var a = id != null ? index.accounts.Find(x => x.id == id) : null;
            if (a == null) return $"No profile \"{who}\" (see \"profiles\").";
            var online = new List<string>();
            foreach (var l in logins.Values) if (l.account == a) online.Add($"client {l.client}{(l.controller ? "" : " (watching)")}");
            string ban = NetModeration.BanReason(a.id, null);
            return $"Profile {a.id}: {(string.IsNullOrEmpty(a.name) ? "?" : a.name)}, role {NetModeration.RoleName(a.role)}, {a.devices.Count} device(s), " +
                   $"worth {a.worth:N0}, played {a.playSeconds / 3600f:0.#} h, created {a.created}, last seen {a.lastSeen}, " +
                   $"{(online.Count > 0 ? "online: " + string.Join(", ", online) : "offline")}, {(ban != null ? "BLOCKED: " + ban : "not blocked")}.";
        }
        /// <summary>Profiles nobody signed in to for PruneDays (by lastSeen, else created) go: not one that is online, nor
        /// staff (NetModeration roles: an op, admin or the master, e.g. the world's owner). Each leaves its faction like a
        /// deleted profile; its file moves to Pruned/ (the newest KeepPruned kept). Returns how many went.</summary>
        public static int Prune()
        {
            nextPrune = Time.realtimeSinceStartup + PruneEverySeconds;
            if (!Enabled || index == null) return 0;
            var cutoff = DateTime.UtcNow.AddDays(-PruneDays);
            var gone = new List<Account>();
            foreach (var a in index.accounts)
            {
                if (a.role > 0 || IsOnline(a.id)) continue;
                string when = string.IsNullOrEmpty(a.lastSeen) ? a.created : a.lastSeen;
                if (!DateTime.TryParse(when, null, System.Globalization.DateTimeStyles.RoundtripKind, out var seen)) continue;   // unknown: kept
                if (seen.ToUniversalTime() < cutoff) gone.Add(a);
            }
            if (gone.Count == 0) return 0;
            string pruned = Path.Combine(folder, "Pruned");
            try { Directory.CreateDirectory(pruned); } catch (Exception e) { Debug.LogError($"NetProfiles: can't make {pruned}: {e.Message}"); }
            foreach (var a in gone)
            {
                index.accounts.Remove(a);
                NetFactions.OnAccountDeleted(a.id);
                try
                {
                    string file = ProfilePath(a.id);
                    string target = Path.Combine(pruned, a.id + ".json");
                    if (File.Exists(file))
                    {
                        File.Copy(file, target, true);
                        File.SetLastWriteTimeUtc(target, DateTime.UtcNow);   // the prune's time: the newest prunes are kept
                    }
                    // Its accounts.json entry (devices, role, statistics) beside it: "profile restore <id>" puts both back.
                    File.WriteAllText(Path.Combine(pruned, a.id + ".account.json"), JsonUtility.ToJson(a, true));
                    if (File.Exists(file)) File.Delete(file);
                    if (File.Exists(file + ".bak")) File.Delete(file + ".bak");
                }
                catch (Exception e) { Debug.LogError($"NetProfiles: pruning {a.id}'s file failed: {e.Message}"); }
                Debug.Log($"Server: profile {a.id} ({(string.IsNullOrEmpty(a.name) ? "?" : a.name)}) pruned: last seen {a.lastSeen ?? a.created}.");
            }
            SaveIndex();
            // Only the newest pruned files stay.
            try
            {
                var files = new List<FileInfo>(new DirectoryInfo(pruned).GetFiles("*.json"));
                files.RemoveAll(f => f.Name.EndsWith(".account.json", StringComparison.OrdinalIgnoreCase));
                files.Sort((x, y) => y.LastWriteTimeUtc.CompareTo(x.LastWriteTimeUtc));
                for (int i = KeepPruned; i < files.Count; i++)
                {
                    string entry = Path.Combine(pruned, Path.GetFileNameWithoutExtension(files[i].Name) + ".account.json");
                    files[i].Delete();
                    if (File.Exists(entry)) File.Delete(entry);
                }
            }
            catch (Exception e) { Debug.LogError($"NetProfiles: tidying {pruned} failed: {e.Message}"); }
            Debug.Log($"Server: {gone.Count} profile(s) unused for {PruneDays} days pruned; {index.accounts.Count} left.");
            return gone.Count;
        }
        /// <summary>DedicatedServer's "profile restore &lt;id&gt;": a pruned profile back (its file and its accounts.json entry, so
        /// its devices sign in to it again), its last-seen date reset so the next prune doesn't take it at once.</summary>
        public static string ConsoleRestore(string id)
        {
            if (!Enabled) return "Player profiles are off (-noprofiles).";
            if (index.accounts.Exists(x => x.id == id)) return $"Profile {id} exists already.";
            string pruned = Path.Combine(folder, "Pruned");
            string file = Path.Combine(pruned, id + ".json"), entry = Path.Combine(pruned, id + ".account.json");
            if (!File.Exists(entry)) return $"No pruned profile \"{id}\" in {pruned}.";
            Account a;
            try { a = JsonUtility.FromJson<Account>(File.ReadAllText(entry)); }
            catch (Exception e) { return $"{entry} can't be read: {e.Message}"; }
            if (a == null || a.id != id) return $"{entry} isn't profile {id}.";
            try
            {
                if (File.Exists(file)) { File.Copy(file, ProfilePath(id), true); File.Delete(file); }
                File.Delete(entry);
            }
            catch (Exception e) { return $"Restoring {id} failed: {e.Message}"; }
            a.lastSeen = Now();
            index.accounts.Add(a);
            SaveIndex();
            Debug.Log($"Server: pruned profile {id} restored.");
            return $"Restored profile {id} ({(string.IsNullOrEmpty(a.name) ? "?" : a.name)}). It left its faction when it was pruned.";
        }
        static void DeleteAccount(Account a)
        {
            index.accounts.Remove(a);
            NetFactions.OnAccountDeleted(a.id);
            BackUp(ProfilePath(a.id));
            Debug.Log($"Server: profile {a.id} deleted.");
        }
    }
}
