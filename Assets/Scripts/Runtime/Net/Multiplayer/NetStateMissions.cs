// NetStateMissions.cs
// NetState: the Pirate activity board, the event graphs' bar missions, squad missions (part of the partial NetState; its fields are in NetState.cs).

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
        /// <summary>The session's Pirate activity board as published (JSON; empty = none).</summary>
        public string PirateBoardJson => pirateBoard.Value.ToString();
        /// <summary>Server: the board as everyone's games read it (cut short only past 4096 bytes: 5 reports take ~1 KB).</summary>
        void SetPirateBoard(string json)
        {
            if (!IsServer) return;
            json ??= "";
            if (System.Text.Encoding.UTF8.GetByteCount(json) > 4000) return;
            if (pirateBoard.Value.ToString() != json) pirateBoard.Value = json;
        }
        /// <summary>The orbit's authority: report 'id' destroyed; 'killer' = the client whose kill it was (the sender itself
        /// for its own kill or an NPC's). Checked against the server's board and the sender's orbit. The bounty is paid here,
        /// never by the sender's game: to the killer, or split evenly across the killer's squad (every member online,
        /// wherever they are; players' suggestion).</summary>
        [Rpc(SendTo.Server)]
        public void WreckHackedRpc(int station, string name, RpcParams rpc = default)
        {
            // A player's game hacked a wreck in its orbit: the events' "on hacked" (checked: the sender is in that orbit).
            ulong client = rpc.Receive.SenderClientId;
            if (!NetRateLimit.Allow(client, NetRateLimit.Kind.Kill) || !NetGuard.InOrbit(client, station)) return;
            name = name ?? "";
            if (name.Length > 64) name = name.Substring(0, 64);
            var pilot = GoF2Remake.Events.EventHost.Find(client);
            if (pilot != null) GoF2Remake.Events.EventRunner.OnWreckHacked(pilot, station, name);
        }
        [Rpc(SendTo.Server)]
        public void PadDockedRpc(int station, RpcParams rpc = default)
        {
            // A player's game docked at a station's pad outside: the events' "on pad" (checked: the sender is in that orbit).
            ulong client = rpc.Receive.SenderClientId;
            if (!NetRateLimit.Allow(client, NetRateLimit.Kind.Kill) || !NetGuard.InOrbit(client, station)) return;
            var pilot = GoF2Remake.Events.EventHost.Find(client);
            if (pilot != null) GoF2Remake.Events.EventRunner.OnPadDocked(pilot, station);
        }
        [Rpc(SendTo.Server)]
        public void PirateSightingDoneRpc(int id, ulong killer, RpcParams rpc = default)
        {
            ulong client = rpc.Receive.SenderClientId;
            if (!NetRateLimit.Allow(client, NetRateLimit.Kind.Kill)) return;
            int bounty = PirateReports.ServerDone(id, station => NetGuard.InOrbit(client, station), SetPirateBoard);
            if (bounty <= 0) return;
            if (!NetworkManager.ConnectedClients.ContainsKey(killer)) killer = client;
            var team = new List<ulong>();
            var who = NetPlayer.All.Find(p => p != null && p.IsSpawned && p.OwnerClientId == killer);
            if (who != null && who.SquadId != 0)
                foreach (var p in NetPlayer.All)
                    if (p != null && p.IsSpawned && p.SquadId == who.SquadId && NetworkManager.ConnectedClients.ContainsKey(p.OwnerClientId))
                        team.Add(p.OwnerClientId);
            if (team.Count == 0) team.Add(killer);
            int share = Mathf.Max(1, bounty / team.Count);
            foreach (ulong c in team) PirateBountyRpc(share, team.Count, RpcTarget.Single(c, RpcTargetUse.Temp));
        }
        /// <summary>A reported pirate's bounty (or this player's share of it, 'shares' > 1: the squad's): the flight HUD's bounty
        /// line in flight, a chat notice for a share.</summary>
        [Rpc(SendTo.SpecifiedInParams, InvokePermission = RpcInvokePermission.Server)]
        void PirateBountyRpc(int amount, int shares, RpcParams rpc = default)
        {
            if (amount <= 0) return;
            var traffic = FindAnyObjectByType<GoF2Remake.World.Traffic>();
            if (traffic != null) traffic.PayBounty(amount);
            else { Session.Credits += amount; GoF2Remake.UI.UiHooks.RefreshStationCredits?.Invoke(); }
            if (shares > 1)
                NetChat.Notice(string.Format(Localization.Extra("pirateBountyShared", "Pirate bounty shared with your squad: +{0} each ({1} pilots)."),
                                             GoF2Remake.UI.ItemInfo.Credits(amount), shares));
            NetProfileClient.Upload();
        }
        /// <summary>Server: credits from the faction bank to the player's game.</summary>
        internal void Grant(ulong client, int amount)
        {
            if (IsServer) GrantRpc(amount, RpcTarget.Single(client, RpcTargetUse.Temp));
        }
        [Rpc(SendTo.SpecifiedInParams, InvokePermission = RpcInvokePermission.Server)]
        void GrantRpc(int amount, RpcParams rpc = default) => NetFactionsClient.OnGrant(amount);
        /// <summary>Server: the toll the player's game paid at 'station' wasn't taken (its profile's saved game hasn't the
        /// credits): 'amount' back to the game, and the toll unpaid.</summary>
        internal void TollRefused(ulong client, int station, int amount)
        {
            if (IsServer) TollRefusedRpc(station, amount, RpcTarget.Single(client, RpcTargetUse.Temp));
        }
        [Rpc(SendTo.SpecifiedInParams, InvokePermission = RpcInvokePermission.Server)]
        void TollRefusedRpc(int station, int amount, RpcParams rpc = default) => NetFactionsClient.OnTollRefused(station, amount);
        /// <summary>A pilot calls their squad for help ('on') or is safe again: a notice to the squadmates with where (NetDistress).</summary>
        [Rpc(SendTo.Server)]
        public void DistressRpc(bool on, RpcParams rpc = default)
        {
            if (!NetRateLimit.Allow(rpc.Receive.SenderClientId, NetRateLimit.Kind.Request)) return;
            var from = NetSquad.Find(rpc.Receive.SenderClientId);
            if (from == null || from.SquadId == 0) return;
            var st = NetGame.Db.Stations.Find(s => s.index == from.Station);
            string text = on
                ? string.Format(Localization.Extra("mpDistressNews", "⚠ {0} calls for help at {1} ({2})! The squad window's Help, or /assist {0}."),
                                from.DisplayName, st?.name ?? "?", st?.systemName ?? "?")
                : string.Format(Localization.Extra("mpDistressOver", "{0} is safe again."), from.DisplayName);
            foreach (var p in NetPlayer.All)
                if (p != null && p.IsSpawned && p != from && p.SquadId == from.SquadId) Notify(p.OwnerClientId, text);
            Debug.Log($"Server: {from.DisplayName} {(on ? "calls for help" : "is safe again")} at {st?.name}.");
        }
        /// <summary>A pilot of another faction paid the toll at 'station' (their game took the credits).</summary>
        [Rpc(SendTo.Server)]
        public void TollPaidRpc(int station, int amount, RpcParams rpc = default)
        {
            if (NetRateLimit.Allow(rpc.Receive.SenderClientId, NetRateLimit.Kind.Request)) NetFactions.OnTollPaid(rpc.Receive.SenderClientId, station, amount);
        }
        /// <summary>An orbit's authority: one of the besieged station's garrison fighters died there (NetOrbit.UpdateGarrison).</summary>
        [Rpc(SendTo.Server)]
        public void GarrisonKillRpc(int station, RpcParams rpc = default)
        {
            if (NetRateLimit.Allow(rpc.Receive.SenderClientId, NetRateLimit.Kind.Kill)) NetFactions.OnGarrisonKill(rpc.Receive.SenderClientId, station);
        }
        /// <summary>Server: a notice for everyone (a claim).</summary>
        internal void Announce(string text)
        {
            if (IsServer && !string.IsNullOrEmpty(text)) NoticeRpc(text);
        }
        // ---- the event graphs' bar missions (EventMissions) ----
        /// <summary>This player docked at 'station': the event missions offered there.</summary>
        internal void RequestEventOffers(int station) => EventOffersUpRpc(station);
        [Rpc(SendTo.Server)]
        void EventOffersUpRpc(int station, RpcParams rpc = default)
        {
            ulong client = rpc.Receive.SenderClientId;
            if (!NetRateLimit.Allow(client, NetRateLimit.Kind.Command) || !NetGuard.Station(station)) return;
            string offers = EventMissions.Offers(station);
            if (offers.Length > 0 && offers.Length < 16000) EventOffersRpc(station, offers, RpcTarget.Single(client, RpcTargetUse.Temp));
        }
        [Rpc(SendTo.SpecifiedInParams, InvokePermission = RpcInvokePermission.Server)]
        void EventOffersRpc(int station, string offers, RpcParams rpc = default) => EventMissions.ReceiveOffers(station, offers);
        /// <summary>The lounge's Okay on an event mission: the server starts it for the squad.</summary>
        internal void AcceptEventMission(string name, int station) => AcceptEventMissionRpc(name ?? "", station);
        [Rpc(SendTo.Server)]
        void AcceptEventMissionRpc(string name, int station, RpcParams rpc = default)
        {
            ulong client = rpc.Receive.SenderClientId;
            if (!NetRateLimit.Allow(client, NetRateLimit.Kind.Command) || name.Length == 0 || name.Length > 64 || !NetGuard.Station(station)) return;
            EventMissions.OnAccept(client, name, station);
        }
        /// <summary>Server: a team member's mission started ('payload': the offer and who took it) or ended ('payload': its name).</summary>
        internal void SendEventMission(ulong client, bool started, string payload) =>
            EventMissionRpc(started, payload ?? "", RpcTarget.Single(client, RpcTargetUse.Temp));
        [Rpc(SendTo.SpecifiedInParams, InvokePermission = RpcInvokePermission.Server)]
        void EventMissionRpc(bool started, string payload, RpcParams rpc = default)
        {
            if (started) EventMissions.OnStarted(payload);
            else EventMissions.OnEnded(payload);
        }
        [Rpc(SendTo.SpecifiedInParams, InvokePermission = RpcInvokePermission.Server)]
        void JoinedSquadRpc(RpcParams rpc = default) => NetMissions.OnJoinedSquad();
        [Rpc(SendTo.SpecifiedInParams, InvokePermission = RpcInvokePermission.Server)]
        void LeftSquadRpc(RpcParams rpc = default) => NetMissions.OnLeftSquad();
        [Rpc(SendTo.SpecifiedInParams, InvokePermission = RpcInvokePermission.Server)]
        void SendMissionToRpc(ulong client, RpcParams rpc = default) => NetMissions.SendTo(client);
        /// <summary>A squad member's mission for one player (the squad's new member): that member joins the mission's team.</summary>
        [Rpc(SendTo.Server)]
        public void ShareMissionToRpc(string json, ulong client, RpcParams rpc = default)
        {
            ulong sender = rpc.Receive.SenderClientId;
            if (!NetRateLimit.Allow(sender, NetRateLimit.Kind.Mission)) return;
            var from = NetSquad.Find(sender);
            var to = NetSquad.Find(client);
            if (from == null || to == null || from == to || from.SquadId == 0 || to.SquadId != from.SquadId) return;
            if (!ParseMission(json, out var m)) { NetRateLimit.Reject(sender, "a malformed mission"); return; }
            var record = Register(m, from);   // a mission the holder had alone becomes the squad's
            if (record == null || record.ended) return;
            record.members.Add(client);
            ReceiveMissionRpc(json, from.DisplayName, true, RpcTarget.Single(client, RpcTargetUse.Temp));
        }
        /// <summary>The host is closing the session: its reason, before the connection goes (NetGame.Shutdown).</summary>
        [Rpc(SendTo.NotServer, InvokePermission = RpcInvokePermission.Server)]
        public void SessionEndingRpc(string reason) => NetGame.OnHostEnding(reason);
        /// <summary>The mission 'netId' whose team 'client' belongs to, null = none.</summary>
        MissionRecord RecordOf(long netId, ulong client)
        {
            if (netId == 0 || !missions.TryGetValue(netId, out var list)) return null;
            foreach (var r in list) if (r.members.Contains(client)) return r;
            return null;
        }
        /// <summary>The record of 'from's mission 'm', made now with 'from' in its team if there is none.</summary>
        MissionRecord Register(FreelanceMission m, NetPlayer from)
        {
            var record = RecordOf(m.netId, from.OwnerClientId);
            if (record != null) return record;
            if (!missions.TryGetValue(m.netId, out var list)) missions[m.netId] = list = new List<MissionRecord>();
            if (list.Count >= 8) return null;   // ids are random: more teams on one id is a client making them up
            int reward = Mathf.Clamp(m.reward, 0, MaxMissionPay);
            record = new MissionRecord
            {
                reward = reward, wager = m.type == MissionType.Challenge ? reward : 0, amount = Mathf.Clamp(m.amount, 0, 1023),
            };
            record.members.Add(from.OwnerClientId);
            list.Add(record);
            return record;
        }
        /// <summary>A mission as a client sends it: bounded in length, readable, with an id.</summary>
        static bool ParseMission(string json, out FreelanceMission m)
        {
            m = null;
            if (string.IsNullOrEmpty(json) || json.Length > NetGuard.MaxMissionJson) return false;
            try { m = JsonUtility.FromJson<FreelanceMission>(json); } catch (System.Exception) { return false; }
            return m != null && !m.IsEmpty && m.netId != 0;
        }
        /// <summary>A player's mission for their squadmates (a new one or an update).</summary>
        [Rpc(SendTo.Server)]
        public void ShareMissionRpc(string json, bool isNew, RpcParams rpc = default)
        {
            ulong client = rpc.Receive.SenderClientId;
            if (!NetRateLimit.Allow(client, NetRateLimit.Kind.Mission)) return;
            var from = NetSquad.Find(client);
            if (from == null || from.SquadId == 0) return;
            if (!ParseMission(json, out var m)) { NetRateLimit.Reject(client, "a malformed mission"); return; }
            long netId = m.netId;
            if (isNew)
            {
                // The host checks the acceptance too: the whole squad docked at the sender's station, and no other member's
                // new mission a moment ago (two accepting at once would swap missions): else the sender's is refused.
                string refusal = null;
                foreach (var p in NetPlayer.All)
                    if (p != null && p != from && p.IsSpawned && p.SquadId == from.SquadId && (p.Where != NetPlayer.Place.Hangar || p.Station != from.Station))
                        refusal = Localization.Extra("mpMissionSquadHere", "The whole squad must be docked at this station to accept a mission.");
                if (squadAccepts.TryGetValue(from.SquadId, out var last) && last.netId != netId && Time.unscaledTime - last.time < 3f)
                    refusal = string.Format(Localization.Extra("mpMissionAtOnce", "{0} accepted a squad mission at the same moment."), last.who);
                var record = refusal == null ? Register(m, from) : null;
                if (refusal == null && record == null) refusal = Localization.Extra("mpMissionRefused", "The squad mission could not be shared.");
                if (refusal != null) { MissionRefusedRpc(netId, refusal, RpcTarget.Single(from.OwnerClientId, RpcTargetUse.Temp)); return; }
                squadAccepts[from.SquadId] = (netId, Time.unscaledTime, from.DisplayName);
                // The squad, every member of which gets it now, is its team.
                foreach (var p in NetPlayer.All)
                    if (p != null && p.IsSpawned && p.SquadId == from.SquadId) record.members.Add(p.OwnerClientId);
            }
            else
            {
                // An update (the return trip): only of a mission the sender's team has.
                var record = RecordOf(netId, client);
                if (record == null || record.ended) return;
            }
            foreach (var p in NetPlayer.All)
                if (p != null && p != from && p.IsSpawned && p.SquadId == from.SquadId)
                    ReceiveMissionRpc(json, from.DisplayName, isNew, RpcTarget.Single(p.OwnerClientId, RpcTargetUse.Temp));
        }
        [Rpc(SendTo.SpecifiedInParams, InvokePermission = RpcInvokePermission.Server)]
        void ReceiveMissionRpc(string json, string from, bool isNew, RpcParams rpc = default) => NetMissions.Receive(json, from, isNew);
        [Rpc(SendTo.SpecifiedInParams, InvokePermission = RpcInvokePermission.Server)]
        void MissionRefusedRpc(long netId, string reason, RpcParams rpc = default) => NetMissions.OnRefused(netId, reason);
        /// <summary>The squad's mission (the sender's squad, and whoever of its team still holds that mission) other than the
        /// sender. (Before, anyone whose MissionHeld matched: a modified client could read a stranger's mission id and fail
        /// or abandon it for them.)</summary>
        List<NetPlayer> MissionTeam(NetPlayer from, long netId, MissionRecord record)
        {
            var list = new List<NetPlayer>();
            foreach (var p in NetPlayer.All)
                if (p != null && p != from && p.IsSpawned
                    && ((from.SquadId != 0 && p.SquadId == from.SquadId) || (record.members.Contains(p.OwnerClientId) && p.MissionHeld == netId)))
                    list.Add(p);
            return list;
        }
        /// <summary>A squad mission's end (NetMissions.Result: success with each member's share, failure, abandoned) for the
        /// rest of the squad: only from its team, once, the share bounded by the mission (a success's by its reward, a
        /// failure's by its Challenge wager, an abandon's none), so nobody can pay or charge other players at will.</summary>
        [Rpc(SendTo.Server)]
        public void MissionResultRpc(long netId, int result, int share, RpcParams rpc = default)
        {
            ulong client = rpc.Receive.SenderClientId;
            if (!NetRateLimit.Allow(client, NetRateLimit.Kind.Mission)) return;
            var from = NetSquad.Find(client);
            var record = from != null ? RecordOf(netId, client) : null;
            if (record == null || record.ended) return;   // a mission held alone (nobody else has it), or ended already
            switch (result)
            {
                case NetMissions.Success: share = Mathf.Clamp(share, 0, record.MaxShare); break;
                case NetMissions.Failure: share = Mathf.Clamp(share, 0, record.wager); break;
                case NetMissions.Abandoned: share = 0; break;
                default: NetRateLimit.Reject(client, $"mission result {result}"); return;
            }
            record.ended = true;
            record.endedAt = Time.unscaledTime;
            foreach (var p in MissionTeam(from, netId, record))
                MissionResultToRpc(netId, result, share, from.DisplayName, RpcTarget.Single(p.OwnerClientId, RpcTargetUse.Temp));
        }
        [Rpc(SendTo.SpecifiedInParams, InvokePermission = RpcInvokePermission.Server)]
        void MissionResultToRpc(long netId, int result, int share, string from, RpcParams rpc = default)
            => NetMissions.OnResult(netId, result, share, from);
        /// <summary>The squad mission's shared progress ('status' + delta: delivered ore, the captured container, the Informer's
        /// 1 / 1000): only from its team, in the steps a game sends.</summary>
        [Rpc(SendTo.Server)]
        public void MissionStatusRpc(long netId, int delta, RpcParams rpc = default)
        {
            ulong client = rpc.Receive.SenderClientId;
            if (!NetRateLimit.Allow(client, NetRateLimit.Kind.Mission)) return;
            var from = NetSquad.Find(client);
            var record = from != null ? RecordOf(netId, client) : null;
            if (record == null || record.ended) return;
            if (delta < 1 || delta > 1000) { NetRateLimit.Reject(client, $"mission progress {delta}"); return; }
            foreach (var p in MissionTeam(from, netId, record))
                MissionStatusToRpc(netId, delta, RpcTarget.Single(p.OwnerClientId, RpcTargetUse.Temp));
        }
        [Rpc(SendTo.SpecifiedInParams, InvokePermission = RpcInvokePermission.Server)]
        void MissionStatusToRpc(long netId, int delta, RpcParams rpc = default) => NetMissions.OnStatus(netId, delta);
        /// <summary>Host: 'gone' disconnects; what they carried for the mission (containers, passengers) goes to a member of its
        /// team still holding it (their squad first), so the squad keeps a mission it can finish. Each count at most the
        /// mission's amount (MissionCargo is the leaving game's word).</summary>
        public void HandOverMission(NetPlayer gone)
        {
            if (!IsServer || gone == null || gone.MissionHeld == 0 || gone.MissionCargo == 0) return;
            long netId = gone.MissionHeld;
            var record = RecordOf(netId, gone.OwnerClientId);
            if (record == null || record.ended) return;
            NetPlayer to = null;
            foreach (var p in NetPlayer.All)
                if (p != null && p != gone && p.IsSpawned && p.MissionHeld == netId && record.members.Contains(p.OwnerClientId)
                    && (to == null || (p.SquadId == gone.SquadId && to.SquadId != gone.SquadId))) to = p;
            if (to == null) return;
            int cap = Mathf.Clamp(record.amount, 1, 1023), cargo = gone.MissionCargo;
            int a = Mathf.Min(cargo & 1023, cap), b = Mathf.Min(cargo >> 10 & 1023, cap), c = Mathf.Min(cargo >> 20 & 1023, cap);
            TakeMissionCargoRpc(netId, a | b << 10 | c << 20, gone.DisplayName, RpcTarget.Single(to.OwnerClientId, RpcTargetUse.Temp));
        }
        [Rpc(SendTo.SpecifiedInParams, InvokePermission = RpcInvokePermission.Server)]
        void TakeMissionCargoRpc(long netId, int cargo, string from, RpcParams rpc = default) => NetMissions.TakeCargo(netId, cargo, from);
    }
}
