// NetFactionsCommands.cs
// NetFactions: names and the chat commands (part of the partial NetFactions; its fields are in NetFactions.cs).

using System;
using System.Collections.Generic;
using System.IO;
using System.Text;
using GoF2Remake.Data;
using UnityEngine;

namespace GoF2Remake.Multiplayer
{
    public static partial class NetFactions
    {
        // ---- names --------------------------------------------------------------------------------------------
        /// <summary>NetProfiles: a player signed in (or linked): their faction's tag on their name, its home.</summary>
        public static void OnLogin(ulong client) => RefreshTag(client);
        static void RefreshTag(ulong client)
        {
            var p = NetSquad.Find(client);
            if (p == null) return;
            p.SetFactionTag(OfClient(client)?.tag ?? "");
            p.SetFactionHome(HomeOf(NetProfiles.AccountOf(client)));
        }
        static void RefreshTags(string account) { foreach (ulong c in NetProfiles.ClientsOf(account)) RefreshTag(c); }
        static void Tell(string account, string text) { foreach (ulong c in NetProfiles.ClientsOf(account)) NetState.Instance?.Notify(c, text); }
        static void TellFaction(Faction faction, string text) { foreach (var m in faction.members) Tell(m, text); }
        static string NameOf(string account) => NetProfiles.AccountName(account) ?? account;
        /// <summary>A faction member by the pilot name (online or not; the profile's last name).</summary>
        static string MemberByName(Faction faction, string name)
        {
            foreach (var m in faction.members) if (string.Equals(NameOf(m), name, StringComparison.OrdinalIgnoreCase)) return m;
            return null;
        }
        // ---- chat commands ----------------------------------------------------------------------------------
        /// <summary>NetState.SendChatRpc: a /faction or /f command's answer to its sender; null = not one of them.</summary>
        public static string Command(ulong client, string text)
        {
            var words = text.Trim().Split(new[] { ' ' }, 3, StringSplitOptions.RemoveEmptyEntries);
            string cmd = words.Length > 0 ? words[0].ToLowerInvariant() : "";
            if (cmd != "/faction" && cmd != "/f") return null;
            string me = NetProfiles.AccountOf(client);
            if (me == null) return Localization.Extra("mpFactionGuest", "Factions need a profile on this server (you play as a guest).");
            if (cmd == "/f") return Talk(me, text.Trim().Length > 2 ? text.Trim().Substring(2).Trim() : "");
            string sub = words.Length > 1 ? words[1].ToLowerInvariant() : "info";
            string arg = words.Length > 2 ? words[2].Trim() : "";
            if (NetPlayerStations.ProfileWritesBlocked && sub != "info" && sub != "list" && sub != "claims" && sub != "sieges")
                return "Faction changes are paused while a station transaction is being recovered.";
            switch (sub)
            {
                case "create": return Create(me, arg);
                case "invite": return Invite(me, arg);
                case "join": return Join(me, arg);
                case "leave": return Leave(me);
                case "kick": return Kick(me, arg);
                case "promote": return SetOfficer(me, arg, true);
                case "demote": return SetOfficer(me, arg, false);
                case "leader": return HandOver(me, arg);
                case "disband": return Disband(me);
                case "info": return Info(arg.Length > 0 ? ByTag(arg) : Of(me), arg);
                case "list": return ListText();
                case "deposit": return Deposit(client, me, arg);
                case "withdraw": return Withdraw(client, me, arg);
                case "claim": return ClaimHere(client, me);
                case "unclaim": return Unclaim(client, me);
                case "home": return SetHome(client, me);
                case "claims": return ClaimsText(arg.Length > 0 ? ByTag(arg) : Of(me));
                case "siege": return DeclareSiege(client, me);
                case "sieges": return SiegesText();
                case "garrison": return SetGarrison(client, me, arg);
                case "vat": return SetVat(client, me, arg);
                default:
                    return Localization.Extra("mpFactionHelp", "Faction commands: /faction create TAG Name, invite <pilot>, join TAG, leave, kick <pilot>, " +
                                                            "promote / demote <pilot>, leader <pilot>, disband, info [TAG], list; deposit N, withdraw N; " +
                                                            "claim, unclaim, home, claims [TAG] (docked at the station); garrison <ships> <level> [station]; vat <0-20> [station]; siege (in another faction's orbit), sieges; " +
                                                            "/f <text> talks to your faction.");
            }
        }
        static string Create(string me, string arg)
        {
            if (Of(me) != null) return Localization.Extra("mpFactionAlready", "You are in a faction already: /faction leave first.");
            int space = arg.IndexOf(' ');
            string tag = (space < 0 ? arg : arg.Substring(0, space)).ToUpperInvariant();
            string name = space < 0 ? "" : NetGame.Clean(arg.Substring(space + 1));
            if (name.Length > MaxNameLength) name = name.Substring(0, MaxNameLength);
            if (tag.Length < 2 || tag.Length > 4 || !IsTag(tag) || name.Length == 0)
                return Localization.Extra("mpFactionCreateUsage", "/faction create TAG Name: a tag of 2-4 letters or digits, then the faction's name.");
            if (ByTag(tag) != null) return string.Format(Localization.Extra("mpFactionTagTaken", "The tag {0} is taken."), tag);
            if (list.factions.Exists(c => string.Equals(c.name, name, StringComparison.OrdinalIgnoreCase)))
                return string.Format(Localization.Extra("mpFactionNameTaken", "A faction called {0} exists already."), name);
            var faction = new Faction { id = Guid.NewGuid().ToString("N").Substring(0, 8), name = name, tag = tag, leader = me, created = DateTime.UtcNow.ToString("o") };
            faction.members.Add(me);
            list.factions.Add(faction);
            Save();
            RefreshTags(me);
            Debug.Log($"Server: faction [{tag}] {name} created by {NameOf(me)}.");
            NetNews.Post(NetNews.Kind.Faction, $"New faction in the sector: {NetNews.FactionName(tag, name)}, founded by {NetNews.Safe(NameOf(me))}");
            return string.Format(Localization.Extra("mpFactionCreated", "Faction [{0}] {1} created. /faction invite <pilot> to bring others in."), tag, name);
        }
        static bool IsTag(string tag)
        {
            foreach (char c in tag) if (!(c >= 'A' && c <= 'Z') && !(c >= '0' && c <= '9')) return false;
            return true;
        }
        static string Invite(string me, string who)
        {
            var faction = Of(me);
            if (faction == null || !IsOfficer(faction, me)) return Localization.Extra("mpFactionNotOfficer", "Only a faction's leader and officers can do that.");
            if (faction.members.Count >= MaxMembers) return string.Format(Localization.Extra("mpFactionFull", "The faction is full ({0})."), MaxMembers);
            NetPlayer target = null;
            foreach (var p in NetPlayer.All)
                if (p != null && p.IsSpawned && string.Equals(p.DisplayName, who, StringComparison.OrdinalIgnoreCase)) { target = p; break; }
            string account = target != null ? NetProfiles.AccountOf(target.OwnerClientId) : null;
            if (account == null) return string.Format(Localization.Extra("mpFactionNoPilot", "No pilot with a profile called \"{0}\" online."), who);
            if (Of(account) != null) return string.Format(Localization.Extra("mpFactionTheyHaveOne", "{0} is in a faction already."), target.DisplayName);
            invites[account] = (faction.id, Time.realtimeSinceStartup + InviteSeconds);
            Tell(account, string.Format(Localization.Extra("mpFactionInvited", "{0} invites you to the faction [{1}] {2}: type /faction join {1}."), NameOf(me), faction.tag, faction.name));
            return string.Format(Localization.Extra("mpFactionInviteSent", "Invitation sent to {0} (5 minutes)."), target.DisplayName);
        }
        static string Join(string me, string tag)
        {
            if (Of(me) != null) return Localization.Extra("mpFactionAlready", "You are in a faction already: /faction leave first.");
            var faction = ByTag(tag);
            if (faction == null || !invites.TryGetValue(me, out var inv) || inv.faction != faction.id || inv.until < Time.realtimeSinceStartup)
                return Localization.Extra("mpFactionNoInvite", "You have no invitation from that faction.");
            if (faction.members.Count >= MaxMembers) return string.Format(Localization.Extra("mpFactionFull", "The faction is full ({0})."), MaxMembers);
            invites.Remove(me);
            faction.members.Add(me);
            Save();
            RefreshTags(me);
            TellFaction(faction, string.Format(Localization.Extra("mpFactionJoined", "{0} joined the faction."), NameOf(me)));
            return "";
        }
        static string Leave(string me)
        {
            var faction = Of(me);
            if (faction == null) return Localization.Extra("mpFactionNone", "You aren't in a faction.");
            if (faction.leader == me && faction.members.Count > 1)
                return Localization.Extra("mpFactionLeaderLeave", "Hand the faction over first (/faction leader <pilot>), or /faction disband.");
            if (faction.leader == me && faction.members.Count == 1 && NetPlayerStations.HasFactionMarketplaceClaims(faction.id))
                return "This faction is still named as the payout destination for active station listings. Complete or cancel those listings before leaving.";
            return Remove(faction, me)
                ? Localization.Extra("mpFactionYouLeft", "You left the faction.")
                : "Your faction could not be dissolved safely because its station ownership changes could not be saved.";
        }
        static string Kick(string me, string who)
        {
            var faction = Of(me);
            if (faction == null || !IsOfficer(faction, me)) return Localization.Extra("mpFactionNotOfficer", "Only a faction's leader and officers can do that.");
            string target = MemberByName(faction, who);
            if (target == null) return string.Format(Localization.Extra("mpFactionNoMember", "No faction member called \"{0}\"."), who);
            if (target == me) return Localization.Extra("mpFactionKickSelf", "Use /faction leave.");
            if (target == faction.leader || (faction.officers.Contains(target) && faction.leader != me))
                return Localization.Extra("mpFactionKickRank", "Only the leader can remove an officer, and nobody the leader.");
            Tell(target, string.Format(Localization.Extra("mpFactionKicked", "You were removed from the faction [{0}]."), faction.tag));
            return Remove(faction, target) ? "" :
                "The member could not be removed because the faction's final station ownership changes could not be saved.";
        }
        static string SetOfficer(string me, string who, bool on)
        {
            var faction = Of(me);
            if (faction == null || faction.leader != me) return Localization.Extra("mpFactionNotLeader", "Only the faction's leader can do that.");
            string target = MemberByName(faction, who);
            if (target == null || target == me) return string.Format(Localization.Extra("mpFactionNoMember", "No faction member called \"{0}\"."), who);
            if (on && !faction.officers.Contains(target)) faction.officers.Add(target);
            if (!on) faction.officers.Remove(target);
            Save();
            NetPlayerStations.RefreshAccessForFactionChange();
            TellFaction(faction, string.Format(on ? Localization.Extra("mpFactionPromoted", "{0} is an officer now.") : Localization.Extra("mpFactionDemoted", "{0} is no officer any more."), NameOf(target)));
            return "";
        }
        static string HandOver(string me, string who)
        {
            var faction = Of(me);
            if (faction == null || faction.leader != me) return Localization.Extra("mpFactionNotLeader", "Only the faction's leader can do that.");
            string target = MemberByName(faction, who);
            if (target == null || target == me) return string.Format(Localization.Extra("mpFactionNoMember", "No faction member called \"{0}\"."), who);
            faction.leader = target;
            faction.officers.Remove(target);
            if (!faction.officers.Contains(me)) faction.officers.Add(me);   // the old leader stays an officer
            Save();
            NetPlayerStations.RefreshAccessForFactionChange();
            TellFaction(faction, string.Format(Localization.Extra("mpFactionNewLeader", "{0} leads the faction now."), NameOf(target)));
            return "";
        }
        static string Disband(string me)
        {
            var faction = Of(me);
            if (faction == null || faction.leader != me) return Localization.Extra("mpFactionNotLeader", "Only the faction's leader can do that.");
            if (NetPlayerStations.HasFactionMarketplaceClaims(faction.id))
                return "This faction is still named as the payout destination for active station listings. Complete or cancel those listings before disbanding.";
            if (!NetPlayerStations.PrepareFactionDeparture(me, faction.id, true))
                return "Faction dissolution was cancelled because station ownership could not be saved safely.";
            TellFaction(faction, string.Format(Localization.Extra("mpFactionDisbanded", "The faction [{0}] {1} was disbanded."), faction.tag, faction.name));
            var members = new List<string>(faction.members);
            int claimsBefore = list.claims.Count;
            list.factions.Remove(faction);
            list.claims.RemoveAll(c => c.faction == faction.id);   // its territory is free again (the bank goes with it)
            foreach (var s in new List<Siege>(list.sieges))
                if (s.defender == faction.id) CallOff(s, Localization.Extra("mpSiegeOffDisband", "[{0}] {1} is gone: the siege of {2} is off, the {3:N0} credits stake goes back to [{4}]."), faction.tag, faction.name);
            list.sieges.RemoveAll(s => s.attacker == faction.id || s.defender == faction.id);   // an attacker's stake goes with its bank
            PublishClaims();
            PublishSieges();
            Save();
            foreach (var m in members) RefreshTags(m);
            Debug.Log($"Server: faction [{faction.tag}] {faction.name} disbanded.");
            int freed = claimsBefore - list.claims.Count;
            NetNews.Post(NetNews.Kind.Faction, $"{NetNews.FactionName(faction.tag, faction.name)} is no more"
                + (freed > 0 ? $": {freed} station{(freed == 1 ? "" : "s")} up for grabs" : ""));
            return "";
        }
        static string Talk(string me, string text)
        {
            var faction = Of(me);
            if (faction == null) return Localization.Extra("mpFactionNone", "You aren't in a faction.");
            text = NetChat.Clean(text);
            if (text.Length == 0) return "/f <text>";
            // The chat's Faction channel (NetChat.Channel.Faction), the sender's own copy marked.
            string from = NameOf(me);
            foreach (var m in faction.members)
                foreach (ulong c in NetProfiles.ClientsOf(m)) NetState.Instance?.TellFactionLine(c, from, text, m == me);
            Debug.Log($"[Faction {faction.tag}] {from}: {text}");
            return "";
        }
        static string Info(Faction faction, string asked)
        {
            if (faction == null)
                return asked.Length > 0 ? string.Format(Localization.Extra("mpFactionNoTag", "No faction with the tag {0}."), asked.ToUpperInvariant())
                                        : Localization.Extra("mpFactionNoneHelp", "You aren't in a faction. /faction create TAG Name, or ask a faction for an invitation; /faction list shows them.");
            var sb = new StringBuilder($"[{faction.tag}] {faction.name}: {faction.members.Count} member(s), leader {NameOf(faction.leader)}");
            if (faction.officers.Count > 0) sb.Append(", officers ").Append(string.Join(", ", faction.officers.ConvertAll(NameOf)));
            var online = faction.members.FindAll(m => NetProfiles.IsOnline(m));
            sb.Append(online.Count > 0 ? ". Online: " + string.Join(", ", online.ConvertAll(NameOf)) : ". Nobody online");
            sb.Append($". Bank {faction.bank:N0}");
            var claims = list.claims.FindAll(c => c.faction == faction.id);
            if (claims.Count > 0) sb.Append(". Territory: ").Append(string.Join(", ", claims.ConvertAll(c => StationName(c.station) + (c.station == faction.home ? " (home)" : ""))));
            sb.Append('.');
            return sb.ToString();
        }
        static string ListText()
        {
            if (list == null || list.factions.Count == 0) return Localization.Extra("mpFactionNoFactions", "No factions yet. /faction create TAG Name starts one.");
            var sb = new StringBuilder(Localization.Extra("mpFactionListTitle", "Factions:"));
            foreach (var c in list.factions) sb.Append($"\n[{c.tag}] {c.name}: {c.members.Count}");
            return sb.ToString();
        }
        /// <summary>A member out of the faction (left, kicked, profile deleted); an emptied faction ends, a leaderless one passes on.</summary>
        static bool Remove(Faction faction, string account)
        {
            if (faction == null || string.IsNullOrEmpty(account) || NetPlayerStations.ProfileWritesBlocked) return false;
            if (faction.members.Count == 1 && NetPlayerStations.HasFactionMarketplaceClaims(faction.id))
                return false;
            if (!NetPlayerStations.PrepareFactionDeparture(account, faction.id, faction.members.Count == 1))
                return false;
            faction.members.Remove(account);
            faction.officers.Remove(account);
            invites.Remove(account);
            if (faction.members.Count == 0)
            {
                list.factions.Remove(faction);
                list.claims.RemoveAll(c => c.faction == faction.id);
                PublishClaims();
                Debug.Log($"Server: faction [{faction.tag}] {faction.name} ended (no members).");
            }
            else
            {
                if (faction.leader == account)
                {
                    faction.leader = faction.officers.Count > 0 ? faction.officers[0] : faction.members[0];
                    faction.officers.Remove(faction.leader);
                    TellFaction(faction, string.Format(Localization.Extra("mpFactionNewLeader", "{0} leads the faction now."), NameOf(faction.leader)));
                }
                TellFaction(faction, string.Format(Localization.Extra("mpFactionMemberLeft", "{0} left the faction."), NameOf(account)));
            }
            Save();
            NetPlayerStations.RefreshAccessForFactionChange();
            RefreshTags(account);
            return true;
        }
        /// <summary>NetProfiles: a profile was deleted.</summary>
        public static void OnAccountDeleted(string account)
        {
            var faction = Of(account);
            if (faction != null) Remove(faction, account);
        }

    }
}
