// MultiplayerWindowTabs.cs
// MultiplayerWindow: the tabs (part of the partial MultiplayerWindow; its fields are in MultiplayerWindow.cs).

using System;
using System.Collections.Generic;
using GoF2Remake.Data;
using GoF2Remake.Multiplayer;
using UnityEngine;
using UnityEngine.InputSystem;
using UnityEngine.UIElements;

namespace GoF2Remake.UI
{
    public partial class MultiplayerWindow
    {
        void Rebuild()
        {
            pendingRebuild = false;
            if (body == null) return;
            float y = scroll.scrollOffset.y;
            // The focused button (a controller's place) is found again after the rebuild: by its text and which of that text.
            string focusText = null;
            int focusNth = 0;
            if (window.focusController?.focusedElement is Button fb && body.Contains(fb))
            {
                focusText = fb.text;
                foreach (var other in body.Query<Button>().ToList()) { if (other == fb) break; if (other.text == focusText) focusNth++; }
            }
            body.Clear();
            if (focusText != null) body.schedule.Execute(() => RestoreFocus(focusText, focusNth)).ExecuteLater(1);
            if (tab == Tab.Chat) return;   // the chat pane is built once (its line keeps the focus and the draft)
            if (tab == Tab.Squad) { BuildSquad(); scroll.scrollOffset = new Vector2(0f, y); return; }   // no snapshot needed
            if (tab == Tab.Trade) { BuildTrade(); scroll.scrollOffset = new Vector2(0f, y); return; }
            var s = NetPanel.Latest;
            if (s == null) { var wait = Text(Localization.Extra("mpPanelLoading", "Asking the server..."), 16, Dim); UiBusy.Mark(wait, true); body.Add(wait); return; }
            switch (tab)
            {
                case Tab.Faction: BuildFaction(s); break;
                case Tab.Arena: BuildArena(s); break;
                case Tab.Admin: BuildAdmin(s); break;
                default: BuildProfile(s); break;
            }
            scroll.scrollOffset = new Vector2(0f, y);
        }
        void RestoreFocus(string text, int nth)
        {
            if (!isOpen || body == null) return;
            Button last = null;
            foreach (var b in body.Query<Button>().ToList())
            {
                if (b.text != text) continue;
                last = b;
                if (nth-- == 0) break;
            }
            (last ?? body.Query<Button>().First())?.Focus();
        }
        void BuildFaction(NetPanel.State s)
        {
            if (!s.profiles) { body.Add(Text(Localization.Extra("mpPanelNoProfiles", "Factions need a dedicated server with player profiles."), 16, Dim)); return; }
            if (s.guest) { body.Add(Text(Localization.Extra("mpPanelGuest", "You're a guest here: factions need a profile (Profile tab)."), 16, Dim)); return; }
            foreach (var inv in s.factionInvites)
            {
                var parts = inv.Split('|');
                string tag = parts[0], name = parts.Length > 1 ? parts[1] : "";
                var row = Line($"[{tag}] {name} " + Localization.Extra("mpPanelInvites", "invites you to their faction."), Good);
                if (!s.inFaction) row.Add(Btn(Localization.Extra("mpPanelJoin", "Join"), () => Send($"/faction join {tag}"), "squad-button--accept"));
                body.Add(row);
            }
            if (!s.inFaction)
            {
                Section(Localization.Extra("mpPanelCreate", "Start a faction"));
                var row = Row();
                row.Add(Field(Localization.Extra("mpPanelTag", "Tag"), tagText, 4, 90, v => tagText = v));
                row.Add(Field(Localization.Extra("mpPanelName", "Name"), nameText, NetFactions.MaxNameLength, 260, v => nameText = v));
                row.Add(Btn(Localization.Extra("mpPanelCreateButton", "Create"), () => Send($"/faction create {tagText.Trim()} {nameText.Trim()}"), "squad-button--accept"));
                body.Add(row);
                body.Add(Text(Localization.Extra("mpPanelCreateHint", "A 2-4 character tag shows before members' names. Or ask a faction to invite you."), 14, Dim));
                FactionList(s);
                return;
            }
            bool officer = s.rank >= 1, leader = s.rank >= 2;
            var title = Text($"[{s.factionTag}] {s.factionName}", 24, Accent);
            title.style.unityFontStyleAndWeight = FontStyle.Bold;
            body.Add(title);
            body.Add(Text(s.rank == 2 ? Localization.Extra("mpRankLeader", "You lead this faction.") : s.rank == 1 ? Localization.Extra("mpRankOfficer", "You are an officer.")
                                                                                                                   : Localization.Extra("mpRankMember", "You are a member."), 14, Dim));

            Section(Localization.Extra("mpPanelBank", "Bank"));
            var bank = Row();
            bank.Add(Text($"{s.bank:N0} " + Localization.Extra("mpCredits", "credits"), 18, Color.white));
            bank.Add(Field(Localization.Extra("mpPanelAmount", "Amount"), amountText, 12, 150, v => amountText = v, TouchScreenKeyboardType.NumberPad));
            bank.Add(Btn(Localization.Extra("mpPanelDeposit", "Deposit"), () => Send($"/faction deposit {amountText.Trim()}"), "squad-button--accept"));
            if (officer) bank.Add(Btn(Localization.Extra("mpPanelWithdraw", "Withdraw"), () => Send($"/faction withdraw {amountText.Trim()}"), null));
            body.Add(bank);

            Section(string.Format(Localization.Extra("mpPanelMembers", "Members ({0})"), s.members.Count));
            foreach (var m in s.members)
            {
                string rank = m.rank == 2 ? Localization.Extra("mpRankLeaderShort", "leader") : m.rank == 1 ? Localization.Extra("mpRankOfficerShort", "officer") : "";
                var row = Line($"{(m.online ? "●" : "○")} {m.name}{(rank.Length > 0 ? $"  ({rank})" : "")}", m.online ? Good : Dim);
                bool self = m.name == SelfName(s);
                if (!self && leader)
                {
                    if (m.rank == 0) row.Add(Btn(Localization.Extra("mpPanelPromote", "Promote"), () => Send($"/faction promote {m.name}"), null));
                    if (m.rank == 1) row.Add(Btn(Localization.Extra("mpPanelDemote", "Demote"), () => Send($"/faction demote {m.name}"), null));
                    row.Add(Btn(Localization.Extra("mpPanelMakeLeader", "Make leader"), () => Send($"/faction leader {m.name}"), null));
                }
                if (!self && officer && (leader || m.rank == 0)) row.Add(Btn(Localization.Extra("mpPanelKick", "Kick"), () => Send($"/faction kick {m.name}"), "squad-button--leave"));
                body.Add(row);
            }
            if (officer)
            {
                var free = s.pilots.FindAll(p => !p.self && string.IsNullOrEmpty(p.tag));
                if (free.Count > 0)
                {
                    Section(Localization.Extra("mpPanelInvite", "Invite a pilot"));
                    foreach (var p in free)
                    {
                        var row = Line(p.name, Color.white);
                        row.Add(Btn(Localization.Extra("mpPanelInviteButton", "Invite"), () => Send($"/faction invite {p.name}"), "squad-button--accept"));
                        body.Add(row);
                    }
                }
            }

            Section(string.Format(Localization.Extra("mpPanelTerritory", "Territory ({0} / {1})"), s.claims.Count, s.maxClaims));
            foreach (var c in s.claims)
            {
                body.Add(Line($"{c.name}{(c.home ? "  ⌂ " + Localization.Extra("mpPanelHome", "home") : "")}{(c.sieged ? "  ⚔ " + Localization.Extra("mpPanelSieged", "under siege") : "")}  ·  " +
                              string.Format(Localization.Extra("mpPanelLapses", "lapses in {0:0.#} days without a member docking"), c.daysLeft), c.sieged ? Bad : Color.white));
                GarrisonRow(c, officer);
                VatRow(c, officer);
            }
            if (s.dockedStation >= 0)
            {
                string here = StationName(s.dockedStation);
                bool mine = s.stationHolder == s.factionTag, held = s.stationHolder.Length > 0;
                var row = Line(string.Format(Localization.Extra("mpPanelDockedAt", "Docked at {0}: {1}"), here,
                    !held ? Localization.Extra("mpPanelFree", "free") : mine ? Localization.Extra("mpPanelOurs", "yours") : $"[{s.stationHolder}]"), Accent);
                if (officer)
                {
                    if (!held && s.stationClaimable && s.claims.Count < s.maxClaims)
                        row.Add(Btn(string.Format(Localization.Extra("mpPanelClaim", "Claim ({0:N0})"), s.claimCost), () => Send("/faction claim"), "squad-button--accept"));
                    if (mine && s.home != s.dockedStation) row.Add(Btn(Localization.Extra("mpPanelMakeHome", "Make home"), () => Send("/faction home"), null));
                    if (mine) row.Add(Btn(Localization.Extra("mpPanelUnclaim", "Give up"), () => Send("/faction unclaim"), "squad-button--leave"));
                    if (held && !mine) row.Add(Btn(string.Format(Localization.Extra("mpPanelSiege", "Siege ({0:N0})"), s.siegeCost), () => Send("/faction siege"), "squad-button--leave"));
                }
                body.Add(row);
            }
            if (s.sieges.Length > 0) body.Add(Text(s.sieges, 14, Dim));

            Section(Localization.Extra("mpPanelFactions", "Factions"));
            FactionList(s);

            var end = Row();
            end.style.marginTop = 16;
            if (leader && s.members.Count <= 1 || !leader)
                end.Add(Btn(confirmLeave ? Localization.Extra("mpPanelConfirmLeave", "Really leave?") : Localization.Extra("mpPanelLeave", "Leave the faction"),
                            () => { if (confirmLeave) Send("/faction leave"); else { confirmLeave = true; Rebuild(); } }, "squad-button--leave"));
            if (leader)
                end.Add(Btn(confirmLeave ? Localization.Extra("mpPanelConfirmDisband", "Really disband? The bank is lost") : Localization.Extra("mpPanelDisband", "Disband"),
                            () => { if (confirmLeave) Send("/faction disband"); else { confirmLeave = true; Rebuild(); } }, "squad-button--leave"));
            body.Add(end);
        }
        /// <summary>A claim's garrison (NetFactions): what it is, and for officers steppers for ships / level and Set with the
        /// day's upkeep ("/faction garrison ships level station").</summary>
        void GarrisonRow(NetPanel.ClaimRow c, bool officer)
        {
            (int ships, int level) current = (c.garrisonSize, Mathf.Clamp(c.garrisonLevel, 1, NetFactions.MaxGarrisonLevel));
            (int ships, int level) edit = garrisonEdit.TryGetValue(c.station, out var e) ? e : current;
            string now = c.garrisonSize > 0
                ? string.Format(Localization.Extra("mpPanelGarrison", "Garrison: {0} fighters, level {1} ({2:N0} credits a day)"), c.garrisonSize, current.level,
                                NetFactions.GarrisonCost(c.garrisonSize, current.level))
                : Localization.Extra("mpPanelNoGarrison", "Garrison: none");
            body.Add(Text("    " + now, 14, Dim));
            if (!officer || c.sieged) return;
            var row = Row();
            void Set((int, int) v) { garrisonEdit[c.station] = v; Rebuild(); }
            row.Add(Text("    " + Localization.Extra("mpPanelGarrisonShips", "Fighters"), 14, Color.white));
            row.Add(Btn("−", () => Set((Mathf.Max(0, edit.ships - 1), edit.level)), null));
            row.Add(Text(edit.ships.ToString(), 16, Accent));
            row.Add(Btn("+", () => Set((Mathf.Min(NetFactions.MaxGarrison, edit.ships + 1), edit.level)), null));
            row.Add(Text(Localization.Extra("mpPanelGarrisonLevel", "Level"), 14, Color.white));
            row.Add(Btn("−", () => Set((edit.ships, Mathf.Max(1, edit.level - 1))), null));
            row.Add(Text(edit.level.ToString(), 16, Accent));
            row.Add(Btn("+", () => Set((edit.ships, Mathf.Min(NetFactions.MaxGarrisonLevel, edit.level + 1))), null));
            if (edit != current)
                row.Add(Btn(string.Format(Localization.Extra("mpPanelGarrisonSet", "Set ({0:N0} / day)"), NetFactions.GarrisonCost(edit.ships, edit.level)), () =>
                {
                    garrisonEdit.Remove(c.station);
                    Send($"/faction garrison {edit.ships} {edit.level} {c.station}");
                }, "squad-button--accept"));
            body.Add(row);
        }
        /// <summary>A claim's VAT (NetFactions): its rate and the goods it costs, for officers a stepper (0..MaxVat) and Set
        /// ("/faction vat rate station").</summary>
        void VatRow(NetPanel.ClaimRow c, bool officer)
        {
            int current = Mathf.Clamp(c.vat, 0, NetFactions.MaxVat);
            int edit = vatEdit.TryGetValue(c.station, out int e) ? e : current;
            body.Add(Text("    " + string.Format(Localization.Extra("mpPanelVat", "VAT: {0}% (non-members pay +{1:0.#}%, {0}% fewer goods)"), current, current / 10f), 14, Dim));
            if (!officer) return;
            var row = Row();
            void Set(int v) { vatEdit[c.station] = v; Rebuild(); }
            row.Add(Text("    " + Localization.Extra("mpPanelVatRate", "VAT").OgCase(), 14, Color.white));
            row.Add(Btn("−", () => Set(Mathf.Max(0, edit - 1)), null));
            row.Add(Text(edit + "%", 16, Accent));
            row.Add(Btn("+", () => Set(Mathf.Min(NetFactions.MaxVat, edit + 1)), null));
            if (edit != current)
                row.Add(Btn(Localization.Extra("mpPanelVatSet", "Set"), () =>
                {
                    vatEdit.Remove(c.station);
                    Send($"/faction vat {edit} {c.station}");
                }, "squad-button--accept"));
            body.Add(row);
        }
        void FactionList(NetPanel.State s)
        {
            if (s.factions.Count == 0) { body.Add(Text(Localization.Extra("mpPanelNoFactions", "No factions yet."), 14, Dim)); return; }
            foreach (var c in s.factions)
                body.Add(Text($"[{c.tag}] {c.name}  ·  {string.Format(Localization.Extra("mpPanelFactionRow", "{0} members, {1} stations"), c.members, c.claims)}", 15, Color.white));
        }
        void BuildArena(NetPanel.State s)
        {
            if (s.inMatch) { body.Add(Text(Localization.Extra("mpPanelInMatch", "You are in a match."), 16, Accent)); return; }
            if (s.duelFrom.Length > 0)
            {
                var row = Line(string.Format(Localization.Extra("mpPanelChallenged", "{0} challenges you to a duel{1}."), s.duelFrom,
                                             s.duelVoids ? Localization.Extra("mpArenaWithVoids", " with the Void fighters") : ""), Good);
                row.Add(Btn(Localization.Extra("mpAccept", "Accept"), () => Send("/accept"), "squad-button--accept"));
                row.Add(Btn(Localization.Extra("mpDecline", "Decline"), () => Send("/decline"), null));
                body.Add(row);
            }
            var opt = new Toggle(Localization.Extra("mpPanelVoids", "With the Void fighters (they attack everyone)")) { value = voids };
            opt.RegisterValueChangedCallback(e => voids = e.newValue);
            opt.style.marginTop = 6;
            body.Add(opt);
            string suffix = voids ? " voids" : "";

            Section(Localization.Extra("mpPanelDuel", "Duel (1v1)"));
            body.Add(Text(string.Format(Localization.Extra("mpPanelDuelHint", "First to {0} kills, or the most after {1} min. Both pilots docked."),
                                        NetArena.DuelKills, Mathf.RoundToInt(NetArena.DuelSeconds / 60f)), 14, Dim));
            foreach (var p in s.pilots)
            {
                if (p.self) continue;
                string state = p.inMatch ? Localization.Extra("mpPanelBusy", "in a match") : !p.docked ? Localization.Extra("mpPanelInSpace", "in space") : "";
                var row = Line((p.tag.Length > 0 ? $"[{p.tag}] " : "") + p.name + (state.Length > 0 ? $"  ({state})" : ""), state.Length > 0 ? Dim : Color.white);
                if (state.Length == 0 && s.dockedStation >= 0) row.Add(Btn(Localization.Extra("mpPanelChallenge", "Challenge"), () => Send($"/duel {p.name}{suffix}"), "squad-button--accept"));
                body.Add(row);
            }
            if (s.pilots.Count <= 1) body.Add(Text(Localization.Extra("mpPanelNobody", "Nobody else is online."), 14, Dim));

            Section(Localization.Extra("mpPanelFfa", "Free-for-all"));
            body.Add(Text(string.Format(Localization.Extra("mpPanelFfaHint", "First to {0} kills, or the most after {1} min; up to {2} pilots."),
                                        NetArena.FfaKills, Mathf.RoundToInt(NetArena.FfaSeconds / 60f), NetArena.FfaMaxPlayers), 14, Dim));
            var ffa = Row();
            if (s.queue > 0)
            {
                ffa.Add(Text(string.Format(Localization.Extra("mpPanelQueued", "In the queue{0}: {1} / {2}"), s.queue == 2 ? Localization.Extra("mpArenaWithVoids", " with the Void fighters") : "",
                                           s.queueCount, NetArena.FfaMaxPlayers) + (s.queueStartsIn >= 0f ? "  ·  " + string.Format(Localization.Extra("mpPanelStartsIn", "starts in {0:0} s"), s.queueStartsIn) : ""), 16, Good));
                ffa.Add(Btn(Localization.Extra("mpPanelLeaveQueue", "Leave the queue"), () => Send("/leave"), "squad-button--leave"));
            }
            else if (s.dockedStation >= 0) ffa.Add(Btn(Localization.Extra("mpPanelJoinFfa", "Join"), () => Send("/ffa" + suffix), "squad-button--accept"));
            body.Add(ffa);

            Section(Localization.Extra("mpPanelMatches", "Matches"));
            if (s.matches.Count == 0) body.Add(Text(Localization.Extra("mpArenaNoMatchesShort", "None running."), 14, Dim));
            foreach (var m in s.matches) body.Add(Text(m, 15, Color.white));
            if (s.profiles && s.top.Length > 0)
            {
                Section(Localization.Extra("mpPanelTop", "Leaderboard"));
                body.Add(Text(s.top, 15, Color.white));
            }
        }
        void BuildProfile(NetPanel.State s)
        {
            if (!s.profiles) { body.Add(Text(Localization.Extra("mpPanelNoProfilesHere", "This server doesn't keep player profiles: nothing is saved."), 16, Dim)); return; }
            if (s.guest) body.Add(Text(Localization.Extra("mpPanelGuestLong", "You're a guest: nothing is saved. A link code from another device uses its profile."), 16, Bad));
            else
            {
                body.Add(Text(string.Format(Localization.Extra("mpPanelProfile", "Profile {0}  ·  {1} device(s)"), s.profileId, s.devices), 18, Color.white));
                body.Add(Text(s.controller ? Localization.Extra("mpPanelControls", "This device plays the profile; it saves on docking, every minute and when leaving.")
                                           : Localization.Extra("mpPanelWatches", "Another of your devices plays the profile; this one only watches."), 15, s.controller ? Dim : Bad));
                if (!s.controller) body.Add(Btn(Localization.Extra("mpPanelTakeControl", "Take control (the other device must be docked)"), () => Send("/control"), "squad-button--accept"));
                Section(Localization.Extra("mpPanelOtherDevice", "Use this profile on another device"));
                body.Add(Text(Localization.Extra("mpPanelLinkHint", "Get a code here, then enter it on your other device within 5 minutes."), 14, Dim));
                if (s.controller) body.Add(Btn(Localization.Extra("mpPanelGetCode", "Get a link code"), () => Send("/link"), "squad-button--accept"));
            }
            Section(Localization.Extra("mpPanelLinkHere", "Use another device's profile here"));
            var row = Row();
            row.Add(Field(Localization.Extra("mpPanelCode", "Code"), linkText, 6, 140, v => linkText = v.UiCaps()));
            var f = new Toggle(Localization.Extra("mpPanelForce", "Replace this device's own progress")) { value = force };
            f.RegisterValueChangedCallback(e => force = e.newValue);
            row.Add(f);
            row.Add(Btn(Localization.Extra("mpPanelLink", "Link"), () => Send($"/link {linkText.Trim()}{(force ? " force" : "")}"), "squad-button--accept"));
            body.Add(row);
            if (!s.guest && s.role < NetModeration.Master)
            {
                Section(Localization.Extra("mpPanelClaimServer", "Claim this server"));
                body.Add(Text(Localization.Extra("mpPanelClaimHint", "Server owner: the admin token is in the server's log and admin_token.txt."), 14, Dim));
                var claim = Row();
                claim.Add(Field(Localization.Extra("mpPanelToken", "Admin token"), tokenText, 64, 260, v => tokenText = v));
                claim.Add(Btn(Localization.Extra("mpPanelClaimButton", "Claim"), () => { Send($"/claimadmin {tokenText.Trim()}"); tokenText = ""; }, "squad-button--accept"));
                body.Add(claim);
            }
        }
        /// <summary>Admins: the server's settings, each with its field (or switch) and Save; saved on the server and kept
        /// after a restart (the command line's own options win at a start: said beside them).</summary>
        void BuildSettings(NetPanel.State s)
        {
            if (s.settings.Count == 0) return;
            Section(Localization.Extra("mpPanelSettings", "Server settings"));
            foreach (var row in s.settings)
            {
                string key = row.key;
                var line = Row();
                line.style.marginBottom = 4;
                // The name, then its note dim under it ("after a restart"; bracketed after the name it wrapped every row).
                var labels = new VisualElement();
                labels.style.width = 380;
                labels.Add(Text(row.label, 15, row.cli ? Dim : Color.white));
                string note = row.note + (row.cli ? (row.note.Length > 0 ? " · " : "") + Localization.Extra("mpPanelFromCli", "set by the launcher") : "");
                if (note.Length > 0) labels.Add(Text(note, 14, Dim));
                line.Add(labels);
                if (row.kind == (int)NetServerSettings.Kind.Toggle)
                {
                    bool on = row.value == "on";
                    line.Add(Btn(on ? Localization.Extra("mpPanelOn", "On") : Localization.Extra("mpPanelOff", "Off"), () => Send($"/set {key} {(on ? "off" : "on")}"),
                                 on ? "squad-button--accept" : null));
                }
                else
                {
                    bool password = row.kind == (int)NetServerSettings.Kind.Password;
                    string shown = settingEdits.TryGetValue(key, out var edit) ? edit : password ? "" : row.value;
                    var field = Field(password ? (row.passwordSet ? Localization.Extra("mpPanelPasswordSet", "set (type a new one, - for none)") : Localization.Extra("mpPanelPasswordNone", "none"))
                                               : row.label, shown, password ? NetGame.MaxPasswordLength : 64, 220, v => settingEdits[key] = v);
                    if (password) field.isPasswordField = true;
                    line.Add(field);
                    line.Add(Btn(Localization.Extra("mpPanelSave", "Save"), () =>
                    {
                        if (!settingEdits.TryGetValue(key, out var v) || v.Trim().Length == 0) return;
                        settingEdits.Remove(key);
                        Send($"/set {key} {v.Trim()}");
                    }, "squad-button--accept"));
                }
                body.Add(line);
            }
        }
        /// <summary>A dangerous button: the first press arms it ("Really?"), the second acts.</summary>
        Button Confirm(string label, string key, string command)
        {
            bool armed = confirmKey == key;
            return Btn(armed ? Localization.Extra("mpPanelReally", "Really?") + " " + label : label, () =>
            {
                if (confirmKey == key) { confirmKey = ""; Send(command); }
                else { confirmKey = key; Rebuild(); }
            }, "squad-button--leave");
        }
        static string RoleLabel(int role) => role >= NetModeration.Master ? "  (master)" : role == NetModeration.Admin ? "  (admin)" : role == NetModeration.Op ? "  (op)" : "";
        void BuildAdmin(NetPanel.State s)
        {
            if (s.role < NetModeration.Op) return;
            bool admin = s.role >= NetModeration.Admin, master = s.role >= NetModeration.Master;
            body.Add(Text(master ? Localization.Extra("mpPanelYouMaster", "You are this server's master admin.") : admin ? Localization.Extra("mpPanelYouAdmin", "You are an admin of this server.")
                                                                                                              : Localization.Extra("mpPanelYouOp", "You are an op of this server."), 15, Accent));
            if (admin)
            {
                Section(Localization.Extra("mpPanelServer", "Server"));
                if (s.serverStatus.Length > 0) body.Add(Text(s.serverStatus, 14, Color.white));
                var say = Row();
                say.Add(Field(Localization.Extra("mpPanelAnnounce", "Announcement to everyone"), sayText, 200, 480, v => sayText = v));
                say.Add(Btn(Localization.Extra("mpPanelSend", "Send"), () => { Send($"/say {sayText.Trim()}"); sayText = ""; }, "squad-button--accept"));
                body.Add(say);
                BuildSettings(s);
            }
            if (!s.profiles)
            {
                BuildAdminFresh(s, master);
                return;
            }
            var opts = Row();
            opts.Add(Field(Localization.Extra("mpPanelReason", "Reason"), reasonText, 80, 320, v => reasonText = v));
            opts.Add(Field(Localization.Extra("mpPanelMinutes", "Minutes"), minutesText, 6, 110, v => minutesText = v));
            body.Add(opts);
            body.Add(Text(string.Format(Localization.Extra("mpPanelAdminHint", "Kick: away for the minutes (0 = can rejoin). Ban: for the minutes{0}."),
                                        admin ? Localization.Extra("mpPanelAdminHintAdmin", ", or for good") : string.Format(Localization.Extra("mpPanelAdminHintOp", " (ops: at most {0} hours)"), NetModeration.MaxOpBanMinutes / 60)), 14, Dim));

            Section(Localization.Extra("mpPanelPilotsOnline", "Pilots online"));
            string minutes = int.TryParse(minutesText.Trim(), out int m) && m >= 0 ? m.ToString() : NetModeration.KickMinutes.ToString();
            string reason = reasonText.Trim();
            foreach (var p in s.pilots)
            {
                string rank = RoleLabel(p.role);
                var row = Line((p.tag.Length > 0 ? $"[{p.tag}] " : "") + p.name + rank + (p.self ? "  (" + Localization.Extra("mpPanelYou", "you") + ")" : ""), p.self ? Dim : Color.white);
                if (!p.self && s.role > p.role)
                {
                    string target = "#" + p.client;
                    row.Add(Btn(Localization.Extra("mpPanelKick", "Kick"), () => Send($"/kick {target} {minutes} {reason}"), "squad-button--leave"));
                    row.Add(Btn(Localization.Extra("mpPanelTempBan", "Ban (minutes)"), () => Send($"/tempban {target} {Mathf.Max(1, int.Parse(minutes))} {reason}"), "squad-button--leave"));
                    if (admin)
                    {
                        row.Add(Confirm(Localization.Extra("mpPanelBanForGood", "Ban for good"), "ban" + target, $"/ban {target} {reason}"));
                        if (p.role == NetModeration.Player) row.Add(Btn(Localization.Extra("mpPanelOp", "Make op"), () => Send($"/op {target}"), "squad-button--accept"));
                        if (p.role == NetModeration.Op) row.Add(Btn(Localization.Extra("mpPanelDeop", "Remove op"), () => Send($"/deop {target}"), null));
                        if (master && p.role < NetModeration.Admin) row.Add(Btn(Localization.Extra("mpPanelMakeAdmin", "Make admin"), () => Send($"/admin {target}"), "squad-button--accept"));
                    }
                }
                body.Add(row);
            }

            Section(string.Format(Localization.Extra("mpPanelBans", "Bans ({0})"), s.bans.Count));
            if (s.bans.Count == 0) body.Add(Text(Localization.Extra("mpAdminNoBans", "Nobody is banned."), 14, Dim));
            foreach (var b in s.bans)
            {
                var row = Line($"{b.name}  ·  {b.left}  ·  {b.by}{(b.reason.Length > 0 ? ": " + b.reason : "")}", Color.white);
                string key = b.account.Length > 0 ? b.account : b.name;
                row.Add(Btn(Localization.Extra("mpPanelUnban", "Unban"), () => Send($"/unban {key}"), "squad-button--accept"));
                body.Add(row);
            }
            if (!admin) return;

            Section(Localization.Extra("mpPanelStaff", "Staff"));
            if (s.staff.Count == 0) body.Add(Text(Localization.Extra("mpPanelNoStaff", "Nobody yet."), 14, Dim));
            foreach (var st in s.staff)
            {
                var row = Line($"{(st.online ? "●" : "○")} {st.name}{RoleLabel(st.role)}", st.online ? Good : Dim);
                if (s.role > st.role)
                {
                    if (st.role == NetModeration.Op) row.Add(Btn(Localization.Extra("mpPanelDeop", "Remove op"), () => Send($"/deop {st.name}"), null));
                    if (st.role == NetModeration.Admin && master) row.Add(Btn(Localization.Extra("mpPanelUnadmin", "Remove admin"), () => Send($"/unadmin {st.name}"), null));
                }
                body.Add(row);
            }

            Section(string.Format(Localization.Extra("mpPanelProfiles", "Profiles ({0})"), s.profileRows.Count));
            var find = Row();
            find.Add(Field(Localization.Extra("mpPanelFilter", "Filter by name or id"), filterText, 24, 300, v => filterText = v));
            find.Add(Btn(Localization.Extra("mpPanelFilterButton", "Filter"), Rebuild, null));   // a rebuild per letter would lose the field's focus
            body.Add(find);
            string filter = filterText.Trim();
            int shown = 0;
            foreach (var pr in s.profileRows)
            {
                if (filter.Length > 0 && pr.name.IndexOf(filter, StringComparison.OrdinalIgnoreCase) < 0 && pr.id.IndexOf(filter, StringComparison.OrdinalIgnoreCase) < 0) continue;
                if (++shown > 60) { body.Add(Text(Localization.Extra("mpPanelMore", "More: narrow the filter."), 14, Dim)); break; }
                var row = Line($"{(pr.online ? "●" : "○")} {(pr.name.Length > 0 ? pr.name : "?")}  ·  {pr.id}{RoleLabel(pr.role)}  ·  {pr.devices} dev.  ·  {pr.lastSeen}{(pr.banned ? "  ·  BANNED" : "")}",
                               pr.banned ? Bad : pr.online ? Good : Color.white);
                if (s.role > pr.role)
                {
                    string id = pr.id;
                    if (pr.banned) row.Add(Btn(Localization.Extra("mpPanelUnban", "Unban"), () => Send($"/unban {id}"), "squad-button--accept"));
                    else row.Add(Confirm(Localization.Extra("mpPanelBanForGood", "Ban for good"), "banp" + id, $"/ban {id} {reasonText.Trim()}"));
                    if (pr.role == NetModeration.Player) row.Add(Btn(Localization.Extra("mpPanelOp", "Make op"), () => Send($"/op {id}"), null));
                    if (pr.role == NetModeration.Op) row.Add(Btn(Localization.Extra("mpPanelDeop", "Remove op"), () => Send($"/deop {id}"), null));
                    if (master && pr.role < NetModeration.Admin) row.Add(Btn(Localization.Extra("mpPanelMakeAdmin", "Make admin"), () => Send($"/admin {id}"), null));
                    if (master && pr.role == NetModeration.Admin) row.Add(Btn(Localization.Extra("mpPanelUnadmin", "Remove admin"), () => Send($"/unadmin {id}"), null));
                    if (master && !pr.online) row.Add(Confirm(Localization.Extra("mpPanelDelete", "Delete"), "del" + id, $"/deleteprofile {id}"));
                }
                body.Add(row);
            }

            Section(Localization.Extra("mpPanelFactions", "Factions"));
            if (s.factions.Count == 0) body.Add(Text(Localization.Extra("mpPanelNoFactions", "No factions yet."), 14, Dim));
            foreach (var c in s.factions)
            {
                var row = Line($"[{c.tag}] {c.name}  ·  {string.Format(Localization.Extra("mpPanelFactionRow", "{0} members, {1} stations"), c.members, c.claims)}", Color.white);
                row.Add(Confirm(Localization.Extra("mpPanelDisband", "Disband"), "faction" + c.tag, $"/disband {c.tag}"));
                body.Add(row);
            }
        }
    }
}
