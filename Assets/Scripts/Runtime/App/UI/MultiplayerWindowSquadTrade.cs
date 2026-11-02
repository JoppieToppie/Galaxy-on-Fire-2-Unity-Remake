// MultiplayerWindowSquadTrade.cs
// MultiplayerWindow: the Squad and Trade tabs (part of the partial MultiplayerWindow; its fields are in MultiplayerWindow.cs).

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
        /// <summary>What the Squad tab shows, as a key: rebuilt only when it changes (a rebuilt button loses a press).</summary>
        static string SquadKey()
        {
            var sb = new System.Text.StringBuilder();
            var me = NetPlayer.Local;
            sb.Append(me != null ? $"{me.SquadId}|{me.Station}|{me.InHangar}|{me.InSpace}|{NetDistress.Active}|{NetFollow.Following}" : "-");
            foreach (var m in NetSquad.Members()) sb.Append('|').Append(m.OwnerClientId).Append(m.DisplayName).Append(NetCommands.WhereText(m)).Append(m.Distress);
            foreach (var i in NetSquad.Invites) sb.Append("|i").Append(i.from);
            foreach (var p in NetPlayer.All)
                if (p != null && p.IsSpawned && !p.IsOwner && p.InHangar && me != null && p.Station == me.Station)
                    sb.Append("|p").Append(p.OwnerClientId).Append(p.DisplayName).Append(p.SquadId).Append(NetSquad.WasInvited(p));
            return sb.ToString();
        }
        void BuildSquad()
        {
            squadKey = SquadKey();
            var me = NetPlayer.Local;
            foreach (var inv in new List<NetSquad.Invite>(NetSquad.Invites))
            {
                var row = Line(string.Format(Localization.Extra("mpSquadInvited", "{0} invites you to their squad."), inv.name), Good);
                var invite = inv;
                row.Add(Btn(Localization.Extra("mpAccept", "Accept"), () => { NetSquad.Accept(invite); squadKey = ""; }, "squad-button--accept"));
                row.Add(Btn(Localization.Extra("mpDecline", "Decline"), () => { NetSquad.Decline(invite); squadKey = ""; }, null));
                body.Add(row);
            }
            if (NetSquad.Invites.Count > 0 && NetMissions.AbandonWarning() is string warn && warn.Length > 0) body.Add(Text(warn, 14, Bad));

            var members = NetSquad.Members();
            Section(Localization.Extra("mpSquadTitle", "Your squad"));
            if (members.Count == 0)
                body.Add(Text(Localization.Extra("mpSquadEmptyHint", "Not in a squad. Invite a pilot docked here, or accept an invitation."), 15, Dim));
            else
            {
                foreach (var m in members)
                {
                    var row = Line((m.Distress ? "⚠ " : "") + m.DisplayName + (m.IsOwner ? $"  ({Localization.Extra("mpYou", "you")})" : "") + "  ·  " + NetCommands.WhereText(m),
                                   m.Distress ? Bad : Color.white);
                    var caller = m;
                    if (!m.IsOwner && m.Distress)
                        row.Add(Btn(Localization.Extra("mpHelpButton", "Help"), () => { string msg = NetDistress.Help(caller); if (!string.IsNullOrEmpty(msg)) status.text = msg; squadKey = ""; }, "squad-button--accept"));
                    // Follow them from orbit to orbit (NetFollow), or stop.
                    if (!m.IsOwner)
                        row.Add(Btn(NetFollow.IsFollowing(m) ? Localization.Extra("mpUnfollowButton", "Stop following") : Localization.Extra("mpFollowButton", "Follow"),
                                    () => { status.text = NetFollow.Toggle(caller) ?? ""; squadKey = ""; }, NetFollow.IsFollowing(m) ? null : "squad-button--accept"));
                    body.Add(row);
                }
                var leave = Row();
                // The distress call (NetDistress): in space only; docked the line says so.
                bool inSpace = me != null && me.InSpace;
                if (inSpace || NetDistress.Active)
                    leave.Add(Btn(NetDistress.Active ? Localization.Extra("mpDistressEnd", "End the call") : Localization.Extra("mpDistressCall", "Distress call"),
                                  () => { status.text = NetDistress.Toggle() ?? ""; squadKey = ""; }, NetDistress.Active ? null : "squad-button--leave"));
                leave.Add(Btn(Localization.Extra("mpLeaveSquad", "Leave squad"), () => { NetSquad.Leave(); squadKey = ""; }, "squad-button--leave"));
                body.Add(leave);
                if (!inSpace)
                    body.Add(Text(Localization.Extra("mpSquadDistressHint", "Call for help in space: the Distress call button, or /sos in the chat."), 14, Dim));
            }

            Section(Localization.Extra("mpSquadPilotsHere", "Pilots docked here"));
            int shown = 0;
            foreach (var p in NetPlayer.All)
            {
                if (p == null || !p.IsSpawned || p.IsOwner || me == null || !p.InHangar || !me.InHangar || p.Station != me.Station) continue;
                shown++;
                var row = Line(p.DisplayName, Color.white);
                var target = p;
                if (NetSquad.Same(p, me)) row.Add(Text(Localization.Extra("mpInYourSquad", "In your squad"), 14, Good));
                else if (NetSquad.WasInvited(p)) row.Add(Text(Localization.Extra("mpInvited", "Invited"), 14, Dim));
                else row.Add(Btn(Localization.Extra("mpInvite", "Invite"), () => { NetSquad.InviteTo(target); squadKey = ""; }, "squad-button--accept"));
                body.Add(row);
            }
            if (shown == 0)
                body.Add(Text(me != null && me.InHangar ? Localization.Extra("mpSquadNobodyHere", "No other pilot is docked here: meet at a station to form a squad.")
                                                       : Localization.Extra("mpSquadDockFirst", "Squads form while docked: dock at the same station as the other pilot."), 15, Dim));
        }
        /// <summary>What the Trade tab shows: the trade, the requests, the hold and the wallet, the pilots docked here, and
        /// whether Accept waits out the other side's last change.</summary>
        static string TradeKey()
        {
            var sb = new System.Text.StringBuilder();
            var me = NetPlayer.Local;
            sb.Append(NetTradeClient.Revision).Append('|').Append(Session.Credits).Append('|').Append(Shop.CargoLoad());
            sb.Append(me != null ? $"|{me.Station}|{me.InHangar}" : "|-");
            foreach (var c in Session.Cargo) sb.Append('|').Append(c.item).Append(':').Append(c.amount);
            foreach (var r in NetTradeClient.Requests) sb.Append("|r").Append(r.from);
            var v = NetTradeClient.Current;
            if (v != null) sb.Append("|h").Append(Time.unscaledTime - v.theirChangedAt < NetTradeClient.ChangedHoldSeconds);
            else if (me != null)
                foreach (var p in NetPlayer.All)
                    if (p != null && p.IsSpawned && !p.IsOwner && p.InHangar && p.Station == me.Station)
                        sb.Append("|p").Append(p.OwnerClientId).Append(p.DisplayName).Append(NetTradeClient.WasAsked(p));
            return sb.ToString();
        }
        void BuildTrade()
        {
            tradeKey = TradeKey();
            var me = NetPlayer.Local;
            bool docked = me != null && me.InHangar;
            foreach (var r in new List<NetTradeClient.Request>(NetTradeClient.Requests))
            {
                var row = Line(string.Format(Localization.Extra("mpTradeRequested", "{0} wants to trade with you."), r.name), Good);
                var request = r;
                if (docked) row.Add(Btn(Localization.Extra("mpAccept", "Accept"), () => { NetTradeClient.Accept(request); tradeKey = ""; }, "squad-button--accept"));
                row.Add(Btn(Localization.Extra("mpDecline", "Decline"), () => { NetTradeClient.Decline(request); tradeKey = ""; }, null));
                body.Add(row);
            }
            var v = NetTradeClient.Current;
            if (v == null) { BuildTradePilots(me, docked); return; }

            var title = Text(string.Format(Localization.Extra("mpTradeWith", "Trading with {0}"), v.partnerName), 22, Accent);
            title.style.unityFontStyleAndWeight = FontStyle.Bold;
            body.Add(title);
            body.Add(Text(Localization.Extra("mpTradeHint", "Add credits and goods to your offer. Any change resets both accepts."), 14, Dim));

            bool open = v.phase == NetTrade.Phase.Open;
            var cols = Row();
            cols.style.alignItems = Align.FlexStart;
            cols.style.flexWrap = Wrap.NoWrap;
            var left = Column(cols);
            var right = Column(cols);
            body.Add(cols);

            // This player's offer: the credits (a field) and the goods (-1 / remove).
            var draft = NetTradeClient.Draft;
            left.Add(Heading(Localization.Extra("mpTradeYourOffer", "Your offer") + (v.myAccepted ? "  ✓" : "")));
            var credits = Row();
            credits.Add(Text(Localization.Extra("mpTradeCredits", "Credits"), 16, Color.white));
            var field = Field(Localization.Extra("mpPanelAmount", "Amount"), draft.credits > 0 ? draft.credits.ToString() : "", 10, 150, value =>
            {
                long n = 0;
                foreach (char c in value) if (c >= '0' && c <= '9' && n < int.MaxValue) n = n * 10 + (c - '0');
                NetTradeClient.SetCredits((int)Math.Min(int.MaxValue, n));
            }, TouchScreenKeyboardType.NumberPad);
            field.SetEnabled(open);
            credits.Add(field);
            credits.Add(Text(string.Format(Localization.Extra("mpTradeOfCredits", "of {0:N0}"), Session.Credits), 14, Dim));
            left.Add(credits);
            if (draft.items.Count == 0) left.Add(Text(Localization.Extra("mpTradeNoGoods", "No goods."), 14, Dim));
            foreach (var s in draft.items)
            {
                var row = Line($"{s.amount} t {GameNames.Item(s.item)}", Color.white);
                int item = s.item, amount = s.amount;
                if (open)
                {
                    row.Add(Btn("−1", () => NetTradeClient.AddItem(item, -1), null));
                    row.Add(Btn("×", () => NetTradeClient.AddItem(item, -amount), "squad-button--leave"));
                }
                left.Add(row);
            }

            // The other side's offer, as the server last sent it.
            right.Add(Heading(string.Format(Localization.Extra("mpTradeTheirOffer", "{0}'s offer"), v.partnerName) + (v.theirAccepted ? "  ✓" : "")));
            right.Add(Text(v.theirs.credits > 0 ? $"{v.theirs.credits:N0} " + Localization.Extra("mpCredits", "credits")
                                                : Localization.Extra("mpTradeNoCredits", "No credits."), 16, v.theirs.credits > 0 ? Color.white : Dim));
            if (v.theirs.items.Count == 0) right.Add(Text(Localization.Extra("mpTradeNoGoods", "No goods."), 14, Dim));
            foreach (var s in v.theirs.items) right.Add(Text($"{s.amount} t {GameNames.Item(s.item)}", 16, Color.white));

            // Where it stands, and the buttons.
            bool held = Time.unscaledTime - v.theirChangedAt < NetTradeClient.ChangedHoldSeconds;
            string state; Color colour = Dim;
            if (!open) { state = Localization.Extra("mpTradeExchanging", "Exchanging..."); colour = Accent; }
            else if (held) { state = string.Format(Localization.Extra("mpTradeTheyChanged", "{0} changed their offer."), v.partnerName); colour = Bad; }
            else if (NetTradeClient.Sending) state = Localization.Extra("mpTradeSending", "Sending your offer...");
            else if (v.myAccepted) state = string.Format(Localization.Extra("mpTradeWaiting", "You accepted. Waiting for {0}."), v.partnerName);
            else if (v.theirAccepted) { state = string.Format(Localization.Extra("mpTradeTheyAccepted", "{0} accepted this trade."), v.partnerName); colour = Good; }
            else state = Localization.Extra("mpTradeNobodyAccepted", "Nobody has accepted yet.");
            var stateLine = Text(state, 16, colour);
            stateLine.style.marginTop = 12;
            body.Add(stateLine);
            // What comes in may not fit the hold (the shop doesn't check either: launching does).
            int incoming = 0, outgoing = 0;
            foreach (var s in v.theirs.items) incoming += s.amount;
            foreach (var s in draft.items) outgoing += s.amount;
            int over = Shop.CargoLoad() - outgoing + incoming - Shop.MaxLoad(NetGame.Db);
            if (incoming > 0 && over > 0)
                body.Add(Text(string.Format(Localization.Extra("mpTradeOverload", "{0} t over your hold's capacity: sell or store some before launch."), over), 14, Bad));
            var buttons = Row();
            if (open)
            {
                bool empty = draft.Empty && v.theirs.Empty;
                var accept = Btn(Localization.Extra("mpTradeAccept", "Accept trade"), () => { NetTradeClient.Confirm(); tradeKey = ""; }, "squad-button--accept");
                accept.SetEnabled(!v.myAccepted && !held && !NetTradeClient.Sending && !empty);
                buttons.Add(accept);
                buttons.Add(Btn(Localization.Extra("mpTradeCancel", "Cancel trade"), () => { NetTradeClient.Cancel(); tradeKey = ""; }, "squad-button--leave"));
            }
            buttons.style.marginTop = 6;
            body.Add(buttons);

            // The hold: what can go into the offer.
            if (!open) return;
            Section(Localization.Extra("mpTradeYourCargo", "Your cargo"));
            int shown = 0;
            var seen = new HashSet<int>();
            foreach (var c in Session.Cargo)
            {
                if (c.amount <= 0 || !seen.Add(c.item) || !NetTradeClient.Tradeable(c.item)) continue;
                int have = Shop.CargoOf(c.item), offered = NetTradeClient.OfferedOf(c.item), item = c.item;
                shown++;
                var row = Line($"{GameNames.Item(item)}  ·  {have} t" + (offered > 0 ? "  " + string.Format(Localization.Extra("mpTradeOffered", "({0} offered)"), offered) : ""), Color.white);
                if (offered < have)
                {
                    row.Add(Btn("+1", () => NetTradeClient.AddItem(item, 1), null));
                    if (have - offered > 1) row.Add(Btn("+10", () => NetTradeClient.AddItem(item, 10), null));
                    row.Add(Btn(Localization.Extra("mpTradeAll", "All"), () => NetTradeClient.AddItem(item, have), "squad-button--accept"));
                }
                body.Add(row);
            }
            if (shown == 0) body.Add(Text(Localization.Extra("mpTradeEmptyHold", "Nothing in your hold can be traded. Demount equipment to trade it."), 15, Dim));
        }
        /// <summary>No trade open: the pilots docked here to trade with.</summary>
        void BuildTradePilots(NetPlayer me, bool docked)
        {
            Section(Localization.Extra("mpTradePilots", "Trade with a pilot docked here"));
            if (!docked)
            {
                body.Add(Text(Localization.Extra("mpTradeDockFirst", "Dock at the same station as the other pilot to trade."), 15, Dim));
                return;
            }
            int shown = 0;
            foreach (var p in NetPlayer.All)
            {
                if (p == null || !p.IsSpawned || p.IsOwner || !p.InHangar || p.Station != me.Station) continue;
                shown++;
                var row = Line(p.DisplayName, Color.white);
                var target = p;
                if (NetTradeClient.WasAsked(p)) row.Add(Text(Localization.Extra("mpTradeAsked", "Asked"), 14, Dim));
                else row.Add(Btn(Localization.Extra("mpTradeButton", "Trade"), () => { NetTradeClient.Ask(target); tradeKey = ""; }, "squad-button--accept"));
                body.Add(row);
            }
            if (shown == 0) body.Add(Text(Localization.Extra("mpTradeNobodyHere", "No other pilot is docked here: meet at a station to trade."), 15, Dim));
            body.Add(Text(Localization.Extra("mpTradeWhat", "Trade credits and cargo; ships and story items can't be traded."), 14, Dim));
        }
        /// <summary>A column of the Trade tab's two (the offers side by side).</summary>
        static VisualElement Column(VisualElement row)
        {
            var c = new VisualElement();
            c.style.flexGrow = 1;
            c.style.flexBasis = 0;
            c.style.marginRight = 16;
            c.style.minWidth = 0;
            row.Add(c);
            return c;
        }
    }
}
