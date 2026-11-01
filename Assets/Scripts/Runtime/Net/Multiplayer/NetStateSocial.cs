// NetStateSocial.cs
// NetState: the message of the day, chat, commands, squads, trades, profiles, factions, the station window, arena matches (part of the partial NetState; its fields are in NetState.cs).

using System.Collections.Generic;
using GoF2Remake.Data;
using Unity.Collections;
using Unity.Netcode;
using UnityEngine;
using GoF2Remake.Events;

namespace GoF2Remake.Multiplayer
{
    public partial class NetState
    {
        // ---- the message of the day (NetMotd) -----------------------------------------------------------------
        /// <summary>A player entered the world: the server's MOTD to them.</summary>
        [Rpc(SendTo.Server)]
        public void MotdRequestRpc(RpcParams rpc = default)
        {
            ulong client = rpc.Receive.SenderClientId;
            if (NetRateLimit.Allow(client, NetRateLimit.Kind.Request)) NetMotd.Send(client, false);
        }
        internal void SendMotd(ulong client, string title, string text, string hash, bool force)
        {
            if (IsServer) MotdRpc(title ?? "", text ?? "", hash ?? "", force, RpcTarget.Single(client, RpcTargetUse.Temp));
        }
        [Rpc(SendTo.SpecifiedInParams, InvokePermission = RpcInvokePermission.Server)]
        void MotdRpc(string title, string text, string hash, bool force, RpcParams rpc = default) => NetMotd.Received(title, text, hash, force);
        // ---- chat (NetChat) ---------------------------------------------------------------------------------
        /// <summary>A player's chat line: the host adds who and where, then everyone gets it (NetChat keeps what is theirs).</summary>
        [Rpc(SendTo.Server)]
        public void SendChatRpc(string text, bool global, RpcParams rpc = default)
        {
            ulong client = rpc.Receive.SenderClientId;
            if (!NetRateLimit.Allow(client, NetRateLimit.Kind.Chat)) return;
            if (text == null || text.Length > NetChat.MaxLength * 4) { NetRateLimit.Reject(client, "an overlong chat line"); return; }
            text = NetChat.Clean(text);
            if (text.Length == 0) return;
            var sender = NetSquad.Find(client);
            if (sender == null) return;
            // A command (the station window's buttons send them as chat lines, NetPanel.Command): run like a typed one
            // (NetCommands), answered to the sender only, never shown to the others.
            if (text[0] == '/')
            {
                string body = text.Substring(1).Trim();
                int space = body.IndexOf(' ');
                string name = space < 0 ? body : body.Substring(0, space);
                string reply = NetCommands.RunOnServer(name, space < 0 ? "" : body.Substring(space + 1), sender)
                               ?? string.Format(Localization.Extra("mpCmdUnknown", "Unknown command /{0}. Type /help for the commands you can use."), name);
                if (reply.Length > 0) NoticeTo(sender, reply);
                return;
            }
            Chat(sender, text, global);
        }
        /// <summary>Server: a chat line from 'sender' (null = the server itself, always global), stamped with its name and
        /// location (/g, /l and the console's say: NetCommands).</summary>
        internal void Chat(NetPlayer sender, string text, bool global)
        {
            text = NetChat.Clean(text);
            if (!IsServer || text.Length == 0) return;
            if (sender != null && NetAdmin.IsMuted(sender.OwnerClientId, out string mutedText)) { NoticeTo(sender, mutedText); return; }   // /mute
            if (sender == null) ServerChat(NetCommands.IssuerName(null), text);
            else ChatRpc(sender.OwnerClientId, sender.TaggedName, text, global, sender.Station, sender.InSpace, sender.InHangar);
        }
        /// <summary>Server: a global chat line from the server itself (the dedicated server's say command).</summary>
        public void ServerChat(string from, string text)
        {
            text = NetChat.Clean(text);
            if (IsServer && text.Length > 0) ChatRpc(NetworkManager.ServerClientId, from, text, true, -1, false, false);
        }
        [Rpc(SendTo.Everyone, InvokePermission = RpcInvokePermission.Server)]
        void ChatRpc(ulong sender, string from, string text, bool global, int station, bool inSpace, bool inHangar)
            => NetChat.Receive(sender, from, text, global, station, inSpace, inHangar);
        /// <summary>Server: /w, a private message passed on to that player only, and a copy back to the sender (null = the
        /// server's console, which logs it).</summary>
        internal void Whisper(NetPlayer from, NetPlayer to, string text)
        {
            text = NetChat.Clean(text);
            if (!IsServer || to == null || text.Length == 0) return;
            if (from != null && NetAdmin.IsMuted(from.OwnerClientId, out string mutedText)) { NoticeTo(from, mutedText); return; }   // /mute
            WhisperedRpc(NetCommands.IssuerName(from), text, false, RpcTarget.Single(to.OwnerClientId, RpcTargetUse.Temp));
            if (from != null) WhisperedRpc(to.DisplayName, text, true, RpcTarget.Single(from.OwnerClientId, RpcTargetUse.Temp));
            else Debug.Log($"Server: [To {to.DisplayName}] {text}");
        }
        [Rpc(SendTo.SpecifiedInParams, InvokePermission = RpcInvokePermission.Server)]
        void WhisperedRpc(string other, string text, bool own, RpcParams rpc = default) => NetChat.ReceiveWhisper(other, text, own);
        /// <summary>Server: /s, a line to the sender's squad (the chat's Squad channel), wherever its members are; the
        /// sender's own copy marked.</summary>
        internal void SquadChat(NetPlayer from, string text)
        {
            text = NetChat.Clean(text);
            if (!IsServer || from == null || from.SquadId == 0 || text.Length == 0) return;
            if (NetAdmin.IsMuted(from.OwnerClientId, out string mutedText)) { NoticeTo(from, mutedText); return; }   // /mute
            foreach (var p in NetPlayer.All)
                if (p != null && p.IsSpawned && p.SquadId == from.SquadId)
                    SquadLineRpc(from.TaggedName, text, p == from, RpcTarget.Single(p.OwnerClientId, RpcTargetUse.Temp));
            Debug.Log($"[Squad {from.SquadId}] {from.TaggedName}: {text}");
        }
        [Rpc(SendTo.SpecifiedInParams, InvokePermission = RpcInvokePermission.Server)]
        void SquadLineRpc(string from, string text, bool own, RpcParams rpc = default) => NetChat.ReceiveSquad(from, text, own);
        // ---- server commands (NetCommands) -----------------------------------------------------------------
        /// <summary>A server command typed in the chat (/kick, /tp, /tphere, /admin, /unadmin): run on the server by the same
        /// code as the dedicated server's console (NetCommands.RunOnServer), which checks the sender's rights; the answer goes
        /// back to the sender.</summary>
        [Rpc(SendTo.Server)]
        public void ServerCommandRpc(string name, string args, RpcParams rpc = default)
        {
            ulong client = rpc.Receive.SenderClientId;
            if (!NetRateLimit.Allow(client, NetRateLimit.Kind.Command)) return;
            if (name == null || name.Length > NetGuard.MaxCommandName || (args != null && args.Length > NetGuard.MaxCommandArgs))
            {
                NetRateLimit.Reject(client, "an overlong command");
                return;
            }
            var from = NetSquad.Find(client);
            if (from == null) return;
            string reply = NetCommands.RunOnServer(name, args, from);
            if (!string.IsNullOrEmpty(reply)) NoticeToRpc(reply, RpcTarget.Single(from.OwnerClientId, RpcTargetUse.Temp));
        }
        /// <summary>Server: a notice in everyone's chat.</summary>
        internal void NoticeAll(string text) => NoticeRpc(text);
        /// <summary>Server: a notice in one player's chat.</summary>
        internal void NoticeTo(NetPlayer p, string text)
        {
            if (p != null && !string.IsNullOrEmpty(text)) NoticeToRpc(text, RpcTarget.Single(p.OwnerClientId, RpcTargetUse.Temp));
        }
        /// <summary>Server: an admin's order for player 'who''s game (NetAdmin: /kill, /heal, /give, /credits, /spawn, /ship...).</summary>
        internal void SendAdmin(ulong who, NetAdmin.Order order, int a, int b, int c, string text, string by) =>
            AdminRpc((byte)order, a, b, c, text ?? "", by, RpcTarget.Single(who, RpcTargetUse.Temp));
        /// <summary>Only the server sends these (checked there: the admin's rights, the arguments).</summary>
        [Rpc(SendTo.SpecifiedInParams, InvokePermission = RpcInvokePermission.Server)]
        void AdminRpc(byte order, int a, int b, int c, string text, string by, RpcParams rpc = default)
            => NetAdmin.Apply((NetAdmin.Order)order, a, b, c, text, by);
        /// <summary>Server: player 'who' goes to 'd' (their game moves its own ship, NetTeleport.Go). 'by' = the admin.</summary>
        internal void SendTeleport(ulong who, NetTeleport.Destination d, string by) =>
            TeleportToRpc((byte)d.kind, d.player, d.station, d.hasPos, d.pos, by, RpcTarget.Single(who, RpcTargetUse.Temp));
        /// <summary>Only the server teleports (an admin's /tp, checked there): before, any client could move any player.</summary>
        [Rpc(SendTo.SpecifiedInParams, InvokePermission = RpcInvokePermission.Server)]
        void TeleportToRpc(byte kind, ulong player, int station, bool hasPos, Vector3 pos, string by, RpcParams rpc = default)
            => NetTeleport.Go(new NetTeleport.Destination { kind = (NetTeleport.Kind)kind, player = player, station = station, hasPos = hasPos, pos = pos }, by);
        /// <summary>Server: a player's admin rights (the host's /admin, the dedicated server's admin command).</summary>
        public void SetAdmin(NetPlayer p, bool on)
        {
            if (!IsServer || p == null) return;
            p.SetAdmin(on);
            Debug.Log($"Server: {p.DisplayName} ({p.OwnerClientId}) is {(on ? "now an admin" : "no longer an admin")}.");
        }
        [Rpc(SendTo.Server)]
        public void InviteRpc(ulong target, RpcParams rpc = default)
        {
            ulong client = rpc.Receive.SenderClientId;
            if (!NetRateLimit.Allow(client, NetRateLimit.Kind.Invite)) return;
            Invite(NetSquad.Find(client), NetSquad.Find(target));
        }
        /// <summary>Server: 'from' invites 'to' into their squad (the pilot list's Invite, /invite): only while both are docked
        /// at the same station, like the pilot list shows them.</summary>
        internal void Invite(NetPlayer from, NetPlayer to)
        {
            if (!IsServer || from == null || to == null || NetSquad.Same(from, to)) return;
            if (!from.InHangar || !to.InHangar || from.Station != to.Station) return;
            invites[(from.OwnerClientId, to.OwnerClientId)] = Time.unscaledTime;
            InvitedRpc(from.OwnerClientId, from.DisplayName, RpcTarget.Single(to.OwnerClientId, RpcTargetUse.Temp));
        }
        [Rpc(SendTo.SpecifiedInParams, InvokePermission = RpcInvokePermission.Server)]
        void InvitedRpc(ulong from, string name, RpcParams rpc = default) => NetSquad.OnInvited(from, name);
        /// <summary>The invited player said yes: into the inviter's squad (a new one if the inviter had none).</summary>
        [Rpc(SendTo.Server)]
        public void AcceptInviteRpc(ulong inviter, RpcParams rpc = default)
        {
            ulong client = rpc.Receive.SenderClientId;
            if (!NetRateLimit.Allow(client, NetRateLimit.Kind.Squad)) return;
            var leader = NetSquad.Find(inviter);
            var joiner = NetSquad.Find(client);
            if (leader == null || joiner == null || leader == joiner || NetSquad.Same(leader, joiner)) return;   // squadmates already
            // Only an invitation this player got (and not an old one).
            if (!invites.TryGetValue((inviter, client), out float sent) || Time.unscaledTime - sent > NetSquad.InviteSeconds + InviteGraceSeconds)
            {
                NetRateLimit.Reject(client, $"joining {leader.DisplayName}'s squad without an invitation");
                return;
            }
            // Squads form only in a hangar: both docked at the same station.
            if (!leader.InHangar || !joiner.InHangar || leader.Station != joiner.Station)
            {
                NoticeToRpc(Localization.Extra("mpSquadHangarOnly", "Squads can only be formed while docked in the same hangar."),
                            RpcTarget.Single(joiner.OwnerClientId, RpcTargetUse.Temp));
                return;
            }
            invites.Remove((inviter, client));
            JoinSquad(leader, joiner);
            NetProfiles.SquadJoined(leader.OwnerClientId, joiner.OwnerClientId);   // both profiles remember it
        }
        /// <summary>'joiner' into 'leader''s squad (a new one if the leader has none).</summary>
        void JoinSquad(NetPlayer leader, NetPlayer joiner)
        {
            if (leader.SquadId == 0) leader.SetSquad(nextSquad++);
            joiner.SetSquad(leader.SquadId);
            DissolveSingles();
            // The joiner's own mission goes; the squad's active one (the inviter's first) comes to them.
            JoinedSquadRpc(RpcTarget.Single(joiner.OwnerClientId, RpcTargetUse.Temp));
            NetPlayer holder = leader.MissionHeld != 0 ? leader : null;
            if (holder == null)
                foreach (var p in NetPlayer.All)
                    if (p != null && p != joiner && p.IsSpawned && p.SquadId == leader.SquadId && p.MissionHeld != 0) { holder = p; break; }
            if (holder != null) SendMissionToRpc(joiner.OwnerClientId, RpcTarget.Single(holder.OwnerClientId, RpcTargetUse.Temp));
            SquadNoticeRpc(leader.SquadId, string.Format(Localization.Extra("mpSquadJoined", "{0} joined the squad."), joiner.DisplayName));
        }
        /// <summary>NetProfiles: a player signing in goes back into the squad their profile remembers ('mate' is online and
        /// in it). Unlike an invitation this works anywhere, docked or not.</summary>
        internal void RestoreSquad(NetPlayer mate, NetPlayer joiner)
        {
            if (!IsServer || mate == null || joiner == null || mate == joiner || NetSquad.Same(mate, joiner)) return;
            JoinSquad(mate, joiner);
        }
        [Rpc(SendTo.Server)]
        public void LeaveSquadRpc(RpcParams rpc = default)
        {
            ulong client = rpc.Receive.SenderClientId;
            if (NetRateLimit.Allow(client, NetRateLimit.Kind.Squad)) LeaveSquad(NetSquad.Find(client));
        }
        /// <summary>Server: 'p' leaves their squad (the squad window's Leave, /leave).</summary>
        internal void LeaveSquad(NetPlayer p)
        {
            if (!IsServer || p == null || p.SquadId == 0) return;
            if (Coop) { NoticeToRpc(Localization.Extra("coopNoLeave", "The crew of a co-op campaign stays one squad."), RpcTarget.Single(p.OwnerClientId, RpcTargetUse.Temp)); return; }
            int id = p.SquadId;
            p.SetSquad(0);
            NetProfiles.SquadLeft(p.OwnerClientId);
            LeftSquadRpc(RpcTarget.Single(p.OwnerClientId, RpcTargetUse.Temp));   // the squad's mission leaves with them
            SquadNoticeRpc(id, string.Format(Localization.Extra("mpSquadLeft", "{0} left the squad."), p.DisplayName));
            DissolveSingles();
        }
        [Rpc(SendTo.Everyone, InvokePermission = RpcInvokePermission.Server)]
        void SquadNoticeRpc(int squad, string text)
        {
            if (NetSquad.LocalSquad == squad || (NetPlayer.Local != null && text.Contains(NetPlayer.Local.DisplayName))) NetChat.Notice(text);
        }
        /// <summary>A squad of one is no squad (the others left or disconnected).</summary>
        void DissolveSingles()
        {
            var count = new Dictionary<int, int>();
            foreach (var p in NetPlayer.All)
                if (p != null && p.IsSpawned && p.SquadId != 0) count[p.SquadId] = count.TryGetValue(p.SquadId, out int c) ? c + 1 : 1;
            foreach (var p in NetPlayer.All)
                if (p != null && p.IsSpawned && p.SquadId != 0 && count[p.SquadId] < 2) p.SetSquad(0);
        }
        // ---- trades between players (NetTrade on the server, NetTradeClient on each game) ---------------------------
        /// <summary>A player asks another docked at the same station to trade (the pilot list's Trade, /trade).</summary>
        [Rpc(SendTo.Server)]
        public void TradeRequestRpc(ulong target, RpcParams rpc = default)
        {
            ulong client = rpc.Receive.SenderClientId;
            if (!NetRateLimit.Allow(client, NetRateLimit.Kind.Invite)) return;
            Notify(client, NetTrade.Request(NetSquad.Find(client), NetSquad.Find(target)));
        }
        internal void TradeRequested(ulong to, ulong from, string name)
        {
            if (IsServer) TradeRequestedRpc(from, name, RpcTarget.Single(to, RpcTargetUse.Temp));
        }
        [Rpc(SendTo.SpecifiedInParams, InvokePermission = RpcInvokePermission.Server)]
        void TradeRequestedRpc(ulong from, string name, RpcParams rpc = default) => NetTradeClient.OnRequested(from, name);
        [Rpc(SendTo.Server)]
        public void TradeAcceptRpc(ulong from, RpcParams rpc = default)
        {
            ulong client = rpc.Receive.SenderClientId;
            if (NetRateLimit.Allow(client, NetRateLimit.Kind.PlayerTrade)) NetTrade.Accept(client, from);
        }
        [Rpc(SendTo.Server)]
        public void TradeDeclineRpc(ulong from, RpcParams rpc = default)
        {
            ulong client = rpc.Receive.SenderClientId;
            if (NetRateLimit.Allow(client, NetRateLimit.Kind.PlayerTrade)) NetTrade.Decline(client, from);
        }
        /// <summary>A side's whole offer: credits, then item / amount pairs (NetTrade checks it).</summary>
        [Rpc(SendTo.Server)]
        public void TradeOfferRpc(int id, int[] offer, RpcParams rpc = default)
        {
            ulong client = rpc.Receive.SenderClientId;
            if (NetRateLimit.Allow(client, NetRateLimit.Kind.PlayerTrade)) NetTrade.SetOffer(client, id, offer);
        }
        [Rpc(SendTo.Server)]
        public void TradeConfirmRpc(int id, int version, RpcParams rpc = default)
        {
            ulong client = rpc.Receive.SenderClientId;
            if (NetRateLimit.Allow(client, NetRateLimit.Kind.PlayerTrade)) NetTrade.Confirm(client, id, version);
        }
        [Rpc(SendTo.Server)]
        public void TradeCancelRpc(int id, RpcParams rpc = default)
        {
            ulong client = rpc.Receive.SenderClientId;
            if (NetRateLimit.Allow(client, NetRateLimit.Kind.PlayerTrade)) NetTrade.Cancel(client, id);
        }
        /// <summary>The game paid its offer (or couldn't).</summary>
        [Rpc(SendTo.Server)]
        public void TradePaidRpc(int id, bool ok, RpcParams rpc = default)
        {
            ulong client = rpc.Receive.SenderClientId;
            if (NetRateLimit.Allow(client, NetRateLimit.Kind.PlayerTrade)) NetTrade.Paid(client, id, ok);
        }
        internal void TradeState(ulong to, int id, ulong partner, string partnerName, int station, int version, int[] mine, int[] theirs,
                                 bool myAccepted, bool theirAccepted, byte phase)
        {
            if (IsServer && NetSquad.Find(to) != null) TradeStateRpc(id, partner, partnerName, station, version, mine, theirs, myAccepted, theirAccepted, phase, RpcTarget.Single(to, RpcTargetUse.Temp));
        }
        [Rpc(SendTo.SpecifiedInParams, InvokePermission = RpcInvokePermission.Server)]
        void TradeStateRpc(int id, ulong partner, string partnerName, int station, int version, int[] mine, int[] theirs,
                           bool myAccepted, bool theirAccepted, byte phase, RpcParams rpc = default) =>
            NetTradeClient.OnState(id, partner, partnerName, station, version, mine, theirs, myAccepted, theirAccepted, phase);
        internal void TradePay(ulong to, int id, int[] offer)
        {
            if (IsServer && NetSquad.Find(to) != null) TradePayRpc(id, offer, RpcTarget.Single(to, RpcTargetUse.Temp));
        }
        [Rpc(SendTo.SpecifiedInParams, InvokePermission = RpcInvokePermission.Server)]
        void TradePayRpc(int id, int[] offer, RpcParams rpc = default) => NetTradeClient.OnPay(id, offer);
        internal void TradeDeliver(ulong to, int id, int[] offer, bool refund, string partnerName)
        {
            if (IsServer && NetSquad.Find(to) != null) TradeDeliverRpc(id, offer, refund, partnerName ?? "", RpcTarget.Single(to, RpcTargetUse.Temp));
        }
        [Rpc(SendTo.SpecifiedInParams, InvokePermission = RpcInvokePermission.Server)]
        void TradeDeliverRpc(int id, int[] offer, bool refund, string partnerName, RpcParams rpc = default) =>
            NetTradeClient.OnDeliver(id, offer, refund, partnerName);
        internal void TradeEnded(ulong to, int id, string reason)
        {
            if (IsServer && NetSquad.Find(to) != null) TradeEndedRpc(id, reason ?? "", RpcTarget.Single(to, RpcTargetUse.Temp));
        }
        [Rpc(SendTo.SpecifiedInParams, InvokePermission = RpcInvokePermission.Server)]
        void TradeEndedRpc(int id, string reason, RpcParams rpc = default) => NetTradeClient.OnEnded(id, reason);
        /// <summary>A player's ship was destroyed by another player: everyone hears of it (only of a player who hit them a
        /// moment ago: no notices blaming anyone at will).</summary>
        [Rpc(SendTo.Server)]
        public void DestroyedByRpc(ulong killer, RpcParams rpc = default)
        {
            ulong client = rpc.Receive.SenderClientId;
            if (!NetRateLimit.Allow(client, NetRateLimit.Kind.Kill)) return;
            var victim = NetSquad.Find(client);
            var by = NetSquad.Find(killer);
            if (victim == null || by == null || !HitRecently(victim.NetworkObjectId, killer, 30f)) return;
            NetArena.OnKill(victim.OwnerClientId, by.OwnerClientId);   // an arena match's score
            NetFactions.OnPilotKilled(victim, by);                     // a siege fight: the killer repaired
            NoticeRpc(string.Format(Localization.Extra("mpDestroyedBy", "{0} was destroyed by {1}."), victim.DisplayName, by.DisplayName));
            EventRunner.OnPlayerKilled(by, victim);   // an event's "on pvpkill"
        }
        [Rpc(SendTo.Everyone, InvokePermission = RpcInvokePermission.Server)]
        void NoticeRpc(string text) => NetChat.Notice(text);
        /// <summary>A player's answer to an event's question (EventScreen): checked and run by the server (EventRunner.OnAnswer).</summary>
        internal void SendAnswer(int ask, int choice) => AnswerRpc(ask, choice);
        [Rpc(SendTo.Server)]
        void AnswerRpc(int ask, int choice, RpcParams rpc = default)
        {
            ulong client = rpc.Receive.SenderClientId;
            if (!NetRateLimit.Allow(client, NetRateLimit.Kind.Command) || choice < 0 || choice > 4) return;
            EventRunner.OnAnswer(client, ask, choice);
        }
        [Rpc(SendTo.SpecifiedInParams, InvokePermission = RpcInvokePermission.Server)]
        void NoticeToRpc(string text, RpcParams rpc = default) => NetChat.Notice(text);
        [Rpc(SendTo.SpecifiedInParams, InvokePermission = RpcInvokePermission.Server)]
        void FactionLineRpc(string from, string text, bool own, RpcParams rpc = default) => NetChat.ReceiveFaction(from, text, own);
        /// <summary>Server: a faction member's chat line to one player (the chat's Faction channel).</summary>
        internal void TellFactionLine(ulong client, string from, string text, bool own)
        {
            if (IsServer && !string.IsNullOrEmpty(text)) FactionLineRpc(from, text, own, RpcTarget.Single(client, RpcTargetUse.Temp));
        }
        /// <summary>Server: a notice in one player's chat.</summary>
        internal void Notify(ulong client, string text)
        {
            if (IsServer && !string.IsNullOrEmpty(text)) NoticeToRpc(text, RpcTarget.Single(client, RpcTargetUse.Temp));
        }
        /// <summary>A joining game on a server with profiles: its token for this server ("" = none) and device label.</summary>
        [Rpc(SendTo.Server)]
        public void LoginRpc(string token, string device, string name, RpcParams rpc = default)
        {
            if (NetRateLimit.Allow(rpc.Receive.SenderClientId, NetRateLimit.Kind.Request)) NetProfiles.OnLogin(rpc.Receive.SenderClientId, token, device, name);
        }
        /// <summary>Server: a profile to one player: the header (its new token, if any; its role), then the gzipped chunks
        /// (none = a fresh start). Reliable RPCs to one client arrive in order.</summary>
        internal void SendProfile(ulong client, string token, bool controller, bool guest, string json, int home)
        {
            if (!IsServer) return;
            var parts = NetProfiles.Pack(json);
            int seq = ++profileSeq;
            ProfileHeaderRpc(seq, parts.Count, token ?? "", controller, guest, home, RpcTarget.Single(client, RpcTargetUse.Temp));
            for (int i = 0; i < parts.Count; i++) ProfileChunkRpc(seq, i, parts[i], RpcTarget.Single(client, RpcTargetUse.Temp));
        }
        [Rpc(SendTo.SpecifiedInParams, InvokePermission = RpcInvokePermission.Server)]
        void ProfileHeaderRpc(int seq, int count, string token, bool controller, bool guest, int home, RpcParams rpc = default)
            => NetProfileClient.OnProfileHeader(seq, count, token, controller, guest, home);
        [Rpc(SendTo.SpecifiedInParams, InvokePermission = RpcInvokePermission.Server)]
        void ProfileChunkRpc(int seq, int part, byte[] data, RpcParams rpc = default) => NetProfileClient.OnProfileChunk(seq, part, data);
        /// <summary>A player's profile, a chunk at a time (NetProfileClient.Upload).</summary>
        [Rpc(SendTo.Server)]
        public void UploadChunkRpc(int seq, int part, int count, byte[] data, RpcParams rpc = default)
            => NetProfiles.OnUploadChunk(rpc.Receive.SenderClientId, seq, part, count, data);
        /// <summary>Server: this player's device controls its profile now, or watches.</summary>
        internal void SendRole(ulong client, bool controller)
        {
            if (IsServer) RoleRpc(controller, RpcTarget.Single(client, RpcTargetUse.Temp));
        }
        [Rpc(SendTo.SpecifiedInParams, InvokePermission = RpcInvokePermission.Server)]
        void RoleRpc(bool controller, RpcParams rpc = default) => NetProfileClient.OnRole(controller);
        /// <summary>Server: the old controller sends its profile once more before another device takes over (/control).</summary>
        internal void RequestUpload(ulong client)
        {
            if (IsServer) RequestUploadRpc(RpcTarget.Single(client, RpcTargetUse.Temp));
        }
        [Rpc(SendTo.SpecifiedInParams, InvokePermission = RpcInvokePermission.Server)]
        void RequestUploadRpc(RpcParams rpc = default) => NetProfileClient.Upload(true);
        // ---- factions (NetFactions / NetFactionsClient) -------------------------------------------------------------
        /// <summary>Server: the player's game is asked to pay a faction deposit ('token' answers it).</summary>
        internal void Charge(ulong client, int token, int amount)
        {
            if (IsServer) ChargeRpc(token, amount, RpcTarget.Single(client, RpcTargetUse.Temp));
        }
        [Rpc(SendTo.SpecifiedInParams, InvokePermission = RpcInvokePermission.Server)]
        void ChargeRpc(int token, int amount, RpcParams rpc = default) => NetFactionsClient.OnCharge(token, amount);
        /// <summary>The player's game paid the deposit 'token' (or had too few credits). Its own bucket (Mission), not the
        /// panel's Request one: the window's snapshots share that, and a dropped answer lost credits the game had already
        /// taken. Only a token the server made does anything.</summary>
        [Rpc(SendTo.Server)]
        public void ChargedRpc(int token, bool paid, RpcParams rpc = default)
        {
            if (NetRateLimit.Allow(rpc.Receive.SenderClientId, NetRateLimit.Kind.Mission)) NetFactions.OnCharged(rpc.Receive.SenderClientId, token, paid);
        }
        // ---- the station's Faction / Arena / Profile window (NetPanel) ----------------------------------------
        /// <summary>The window asks for its snapshot.</summary>
        [Rpc(SendTo.Server)]
        public void PanelRequestRpc(RpcParams rpc = default)
        {
            if (NetRateLimit.Allow(rpc.Receive.SenderClientId, NetRateLimit.Kind.Request)) NetPanel.Send(rpc.Receive.SenderClientId);
        }
        /// <summary>Server: a chunk of the snapshot to one player.</summary>
        internal void PanelChunk(ulong client, int seq, int part, int count, byte[] data)
        {
            if (IsServer) PanelChunkRpc(seq, part, count, data, RpcTarget.Single(client, RpcTargetUse.Temp));
        }
        [Rpc(SendTo.SpecifiedInParams, InvokePermission = RpcInvokePermission.Server)]
        void PanelChunkRpc(int seq, int part, int count, byte[] data, RpcParams rpc = default) => NetPanel.OnChunk(seq, part, count, data);
        // ---- arena matches (NetArena / NetArenaClient) -----------------------------------------------------
        /// <summary>Server: the player goes into match 'id' (their game loads the arena).</summary>
        internal void ArenaStart(ulong client, int id, byte kind, int orbitId, int template, bool voids, int slot, int killLimit, float seconds)
        {
            if (IsServer) ArenaStartRpc(id, kind, orbitId, template, voids, slot, killLimit, seconds, RpcTarget.Single(client, RpcTargetUse.Temp));
        }
        [Rpc(SendTo.SpecifiedInParams, InvokePermission = RpcInvokePermission.Server)]
        void ArenaStartRpc(int id, byte kind, int orbitId, int template, bool voids, int slot, int killLimit, float seconds, RpcParams rpc = default)
            => NetArenaClient.OnStart(id, kind, orbitId, template, voids, slot, killLimit, seconds);
        /// <summary>A player's game has the arena loaded.</summary>
        [Rpc(SendTo.Server)]
        public void ArenaReadyRpc(int id, RpcParams rpc = default)
        {
            if (NetRateLimit.Allow(rpc.Receive.SenderClientId, NetRateLimit.Kind.Request)) NetArena.OnReady(rpc.Receive.SenderClientId, id);
        }
        /// <summary>Server: the match's phase, seconds left, scores and a kill-feed line ("" = none) to one player.</summary>
        internal void ArenaState(ulong client, int id, byte phase, float left, ulong[] players, int[] kills, string feed)
        {
            if (IsServer) ArenaStateRpc(id, phase, left, players, kills, feed, RpcTarget.Single(client, RpcTargetUse.Temp));
        }
        [Rpc(SendTo.SpecifiedInParams, InvokePermission = RpcInvokePermission.Server)]
        void ArenaStateRpc(int id, byte phase, float left, ulong[] players, int[] kills, string feed, RpcParams rpc = default)
            => NetArenaClient.OnState(id, phase, left, players, kills, feed);
        /// <summary>Server: the match is over (the result's text); the player's game goes home after showing it.</summary>
        internal void ArenaEnd(ulong client, int id, string text)
        {
            if (IsServer) ArenaEndRpc(id, text, RpcTarget.Single(client, RpcTargetUse.Temp));
        }
        [Rpc(SendTo.SpecifiedInParams, InvokePermission = RpcInvokePermission.Server)]
        void ArenaEndRpc(int id, string text, RpcParams rpc = default) => NetArenaClient.OnEnd(id, text);
        /// <summary>Server: the player left their match (/leave): home at once.</summary>
        internal void ArenaLeave(ulong client)
        {
            if (IsServer) ArenaLeaveRpc(RpcTarget.Single(client, RpcTargetUse.Temp));
        }
        [Rpc(SendTo.SpecifiedInParams, InvokePermission = RpcInvokePermission.Server)]
        void ArenaLeaveRpc(RpcParams rpc = default) => NetArenaClient.OnLeave();
    }
}
