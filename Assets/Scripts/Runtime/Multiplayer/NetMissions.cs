// NetMissions.cs
// Multiplayer bar missions (Freelance) for squads: a squad has one mission, the same for every member (one squad id,
// FreelanceMission.netId), with shared goals:
//   accepting one (Freelance.Accept) needs the whole squad docked at the agent's station (AcceptRefusal); every member
//     gets it (their own mission replaced like accepting a new one); a change (Recovery / Salvage turning into the return
//     trip) updates it for all;
//   progress is shared: anyone's kills count (Target.killedByRemote), the mission's status too (AddStatus: a member's ore
//     delivered to the plant, the container captured by any member); the containers / passengers stay with the member who
//     carries them, so only they deliver Courier / Passenger / a Recovery return trip (CanDeliver), Purchase / Stolen goods
//     anyone with the goods;
//   the mission orbit is built once (SpaceLevel, ShouldRun): by the first member there, whose game runs it
//     (NetPlayer.MissionRun) and shows its ships and junk to everyone in the orbit (NetOrbit);
//   a success (in space or docking) pays every member an equal share (SplitReward, the rest of Freelance.Succeed: standing
//     +5 toward the client, missions completed +1) and ends it for all; a failure ends it too, so does a member's Discard
//     (Abandon).
// Solo players keep the whole reward. The host relays the messages (NetState), to the squad and whoever holds the mission.
// Joining a squad (only while docked in the same hangar, NetState.AcceptInviteRpc) abandons the joining player's own
// mission, and the squad's (the inviter's first) is sent to them. Leaving a squad removes its mission for that player
// (OnLeftSquad); a member leaving with the containers / passengers aboard ends it for the squad.

using System;
using GoF2Remake.Data;
using GoF2Remake.Flight;
using GoF2Remake.UI;
using UnityEngine;

namespace GoF2Remake.Multiplayer
{
    [Unity.Scripting.LifecycleManagement.NoAutoStaticsCleanup]
    public static class NetMissions
    {
        /// <summary>MissionResultRpc's results.</summary>
        public const int Failure = 0, Success = 1, Abandoned = 2;

        static readonly System.Random ids = new System.Random();

        static bool InSession => NetGame.Active && NetState.Instance != null && NetState.Instance.IsSpawned;
        static bool Squad => InSession && NetSquad.InSquad;
        static bool Held(FreelanceMission m) => m != null && !m.IsEmpty && m.netId != 0;

        /// <summary>Freelance.AcceptRefusal: in a squad, every member docked at this station (null = ok).</summary>
        public static string AcceptRefusal()
        {
            if (!Squad) return null;
            foreach (var p in NetSquad.Members())
                if (!p.IsOwner && (p.Where != NetPlayer.Place.Hangar || p.Station != Session.StationIndex))
                    return Localization.Extra("mpMissionSquadHere", "The whole squad must be docked at this station to accept a mission.");
            return null;
        }

        /// <summary>Freelance.Accept: a new squad id, and the mission for every squadmate.</summary>
        public static void OnAccepted(FreelanceMission m)
        {
            if (!NetGame.Active || m == null) return;
            m.netId = ((long)ids.Next() << 31) | (uint)ids.Next();
            if (m.netId == 0) m.netId = 1;
            Share(m, true);
        }

        /// <summary>Sends 'm' to the squadmates: new (their mission becomes it) or an update of theirs.</summary>
        public static void Share(FreelanceMission m, bool isNew)
        {
            if (!Squad || !Held(m)) return;
            NetState.Instance.ShareMissionRpc(JsonUtility.ToJson(m), isNew);
        }

        /// <summary>NetState: the squad's mission from a squadmate (new, or an update of this player's).</summary>
        internal static void Receive(string json, string from, bool isNew)
        {
            FreelanceMission m;
            try { m = JsonUtility.FromJson<FreelanceMission>(json); } catch (Exception) { return; }
            if (!Held(m)) return;
            bool same = Freelance.Active && Freelance.Mission.netId == m.netId;
            if (same && !isNew)
            {
                // An update (the return trip) into the same mission object, so a running orbit keeps it.
                bool returnTrip = m.status == -1 && Freelance.Mission.status != -1;
                JsonUtility.FromJsonOverwrite(json, Freelance.Mission);
                if (returnTrip)
                {
                    // Whoever carries the container keeps it for the client (Freelance.ToReturnTrip's unsaleable mark).
                    Session.Unsaleable.Add(Freelance.SecureContainer);
                    Session.Unsaleable.Add(Freelance.SecureCabin);
                }
                if (returnTrip)
                    NetChat.Notice(string.Format(Localization.Extra("mpMissionReturn",
                        "{0}: the squad has the container, whoever carries it takes it back to the client."), m.Name));
                return;
            }
            if (!isNew || same) return;   // an update of a mission this player doesn't have (any more), or theirs already
            if (Freelance.Active) Freelance.Discard();
            Session.FreelanceMission = m;
            Session.InformerKilled = Session.InformerFailed = false;
            NetChat.Notice(string.Format(Localization.Extra("mpMissionShared", "{0} accepted a squad mission: {1}."), from, m.Name));
            ShowCard(m, from);
        }

        /// <summary>The squad mission a squadmate took, on this player's screen (NetScreen's mission card): its name, who took
        /// it, the client's face and name, the target and the reward.</summary>
        static void ShowCard(FreelanceMission m, string from)
        {
            var db = NetGame.Db;
            var station = db?.Stations.Find(s => s.index == m.target);
            var system = station != null ? db.Systems.Find(s => s.index == station.system) : null;
            var lines = new System.Collections.Generic.List<string>();
            if (!string.IsNullOrEmpty(m.clientName)) lines.Add(string.Format(Localization.Extra("mpMissionCardClient", "Client: {0}"), m.clientName));
            if (station != null)
                lines.Add(string.Format(Localization.Extra("mpMissionCardTarget", "Target: {0}"), system != null ? $"{station.name} ({system.name})" : station.name));
            if (m.Total > 0) lines.Add(string.Format(Localization.Extra("mpMissionCardReward", "Reward: {0}, shared by the squad"), ItemInfo.Credits(m.Total)));
            NetScreen.ShowMissionCard(null, m.Name, string.Format(Localization.Extra("mpMissionCardBy", "Accepted by {0}"), from),
                string.Join("\n", lines), m.clientPortrait);
        }

        /// <summary>The mission's shared status grew by 'delta' here (delivered ore, the captured container): for the squad too.</summary>
        public static void AddStatus(FreelanceMission m, int delta)
        {
            if (!InSession || !Held(m)) return;
            NetState.Instance.MissionStatusRpc(m.netId, delta);
        }

        /// <summary>NetState: a squadmate's progress.</summary>
        internal static void OnStatus(long netId, int delta)
        {
            // status -1 = the return trip: nothing more to count.
            if (!Freelance.Active || Freelance.Mission.netId != netId || Freelance.Mission.status < 0) return;
            if (Freelance.Mission.type == MissionType.Informer)
            {
                // The spy's owner saw it die (1) or another ship die first (1000): the same for the whole squad.
                if (delta >= 1000) { if (!Session.InformerKilled) Session.InformerFailed = true; }
                else if (!Session.InformerFailed) Session.InformerKilled = true;
                return;
            }
            Freelance.Mission.status += delta;
            // Stolen goods: a squadmate bought the documents, so they are gone from this player's shop too.
            if (Freelance.Mission.type == MissionType.StolenGoods) NetStock.RemoveDocuments(Freelance.Mission.target);
        }

        /// <summary>NetPlayer: what this player carries for the mission (containers 116 | 117 << 10 | passengers << 20).</summary>
        public static int PackCargo()
        {
            if (!Freelance.Active) return 0;
            var m = Freelance.Mission;
            // Courier's containers, the return trip's container, and a Recovery / Salvage container already captured.
            bool containers = m.type == MissionType.Courier || (m.type == MissionType.Passenger && m.status == -1)
                              || m.type == MissionType.Recovery || m.type == MissionType.Salvage;
            int a = containers ? Mathf.Min(1023, Story.CargoOf(Freelance.SecureContainer)) : 0;
            int b = containers ? Mathf.Min(1023, Story.CargoOf(Freelance.SecureCabin)) : 0;
            int c = m.type == MissionType.Passenger && m.status != -1 ? Mathf.Min(1023, Session.Passengers) : 0;
            return a | b << 10 | c << 20;
        }

        /// <summary>NetState: a squadmate who carried the mission's containers / passengers disconnected: this player has them now.</summary>
        internal static void TakeCargo(long netId, int cargo, string from)
        {
            if (!Freelance.Active || Freelance.Mission.netId != netId) return;
            int a = cargo & 1023, b = cargo >> 10 & 1023, c = cargo >> 20 & 1023;
            if (a > 0) { Shop.AddToCargo(Freelance.SecureContainer, a); Session.Unsaleable.Add(Freelance.SecureContainer); }
            if (b > 0) { Shop.AddToCargo(Freelance.SecureCabin, b); Session.Unsaleable.Add(Freelance.SecureCabin); }
            if (c > 0) Session.Passengers += c;
            NetChat.Notice(string.Format(Localization.Extra("mpMissionCargo", "{0} left the session: you carry {1}'s cargo now."), from, Freelance.Mission.Name));
        }

        /// <summary>FreelanceOrbit (a squadmate's view of the mission orbit): shows a result as the mission's dialog; true =
        /// shown (no chat line then).</summary>
        internal static Func<int, int, string, bool> ResultView;

        /// <summary>This player carries what a Courier / Passenger / return-trip delivery needs (containers, passengers).</summary>
        static bool Carries(FreelanceMission m)
        {
            if (m.type == MissionType.Courier || (m.type == MissionType.Passenger && m.status == -1)) return Freelance.ContainersAboard() > 0;
            if (m.type == MissionType.Passenger) return Session.Passengers > 0;
            return false;
        }

        /// <summary>Freelance.CheckDocked: may this player deliver 'm' (single player: always)?</summary>
        public static bool CanDeliver(FreelanceMission m) => !NetGame.Active || Carries(m);

        /// <summary>Freelance.Succeed: this player's share of 'pay' (all of it solo); the squadmates get theirs.</summary>
        public static int SplitReward(FreelanceMission m, int pay)
        {
            if (!Squad || !Held(m)) return pay;
            int share = pay / Mathf.Max(1, NetSquad.Members().Count);   // an equal share for every member
            NetState.Instance.MissionResultRpc(m.netId, Success, share);
            return share;
        }

        /// <summary>Freelance.Fail: failed for the squad; a lost Challenge's wager is shared like the reward. Returns this
        /// player's part of 'wager' (all of it solo).</summary>
        public static int Failed(FreelanceMission m, int wager)
        {
            if (!InSession || !Held(m)) return wager;
            int share = Squad ? wager / Mathf.Max(1, NetSquad.Members().Count) : wager;
            NetState.Instance.MissionResultRpc(m.netId, Failure, share);
            return share;
        }

        /// <summary>NetState: the host refused this player's new squad mission (the squad not all docked here, another
        /// member's at the same moment): it goes.</summary>
        internal static void OnRefused(long netId, string reason)
        {
            NetChat.Notice(reason);
            if (Freelance.Active && Freelance.Mission.netId == netId) Freelance.Discard();
        }

        /// <summary>The Missions window's Discard: in a squad it ends the mission for everyone.</summary>
        public static void Abandon()
        {
            if (!InSession || !Freelance.Active || !Held(Freelance.Mission)) return;
            NetState.Instance.MissionResultRpc(Freelance.Mission.netId, Abandoned, 0);
        }

        /// <summary>The Discard question's addition in a squad ("" otherwise).</summary>
        public static string DiscardWarning() => Squad && Freelance.Active && Held(Freelance.Mission)
            ? "\n" + Localization.Extra("mpDiscardSquad", "It ends the mission for the whole squad.") : "";

        /// <summary>NetState: a squadmate finished (or abandoned) the squad's mission.</summary>
        internal static void OnResult(long netId, int result, int share, string from)
        {
            if (!Freelance.Active || Freelance.Mission.netId != netId)
            {
                // A squad member without the mission (it never reached them) still gets their share of a success.
                if (result == Success && share > 0 && Squad)
                {
                    Session.Credits += share;
                    NetChat.Notice(string.Format(Localization.Extra("mpMissionDone", "{0} completed {1}: your share +{2} credits."), from,
                                                 Localization.Extra("mpSquadMission", "the squad's mission"), share));
                }
                return;
            }
            var m = Freelance.Mission;
            // The lost wager, shared: never more than this mission's own wager (the server bounds it too).
            if (result == Failure && m.type == MissionType.Challenge) Session.Credits -= Mathf.Clamp(share, 0, Mathf.Max(0, m.reward));
            bool shown = ResultView != null && ResultView(result, share, from);   // the dialog, before the mission goes
            if (result == Success)
            {
                Standing.ApplyDelict(m.clientRace, -5);   // Standing::applyMissionCompleted
                Session.FreelanceCompleted++;
                Session.Credits += share;
                if (!shown) NetChat.Notice(string.Format(Localization.Extra("mpMissionDone", "{0} completed {1}: your share +{2} credits."), from, m.Name, share));
            }
            else if (shown) { }
            else if (result == Abandoned) NetChat.Notice(string.Format(Localization.Extra("mpMissionAbandonedBy", "{0} abandoned {1}."), from, m.Name));
            else NetChat.Notice(string.Format(Localization.Extra("mpMissionFailed", "{0} failed {1}."), from, m.Name));
            Freelance.Discard();
        }

        /// <summary>NetState: this player joined a squad: their own mission is abandoned (the squad's comes next).</summary>
        internal static void OnJoinedSquad()
        {
            if (!Freelance.Active) return;
            var m = Freelance.Mission;
            if (InSession && Held(m) && Carries(m)) NetState.Instance.MissionResultRpc(m.netId, Abandoned, 0);   // an old squad's cargo leaves with them
            NetChat.Notice(string.Format(Localization.Extra("mpMissionAbandoned", "You abandoned {0} to join the squad."), m.Name));
            Freelance.Discard();
        }

        /// <summary>NetState: this player left their squad: the squad's mission is no longer theirs.</summary>
        internal static void OnLeftSquad()
        {
            if (!Freelance.Active || !Held(Freelance.Mission)) return;
            var m = Freelance.Mission;
            if (InSession && Carries(m)) NetState.Instance.MissionResultRpc(m.netId, Abandoned, 0);   // the containers / passengers leave with them
            NetChat.Notice(string.Format(Localization.Extra("mpMissionLeft", "You left the squad and its mission: {0}."), m.Name));
            Freelance.Discard();
        }

        /// <summary>NetState: send this player's mission to the squad's new member 'client'.</summary>
        internal static void SendTo(ulong client)
        {
            if (!InSession || !Freelance.Active || !Held(Freelance.Mission)) return;
            NetState.Instance.ShareMissionToRpc(JsonUtility.ToJson(Freelance.Mission), client);
        }

        /// <summary>The invitation's warning: what accepting it abandons ("" without a mission).</summary>
        public static string AbandonWarning() => Freelance.Active
            ? string.Format(Localization.Extra("mpInviteAbandons", "Accepting abandons your current mission: {0}."), Freelance.Mission.Name) : "";

        /// <summary>Another player holding this player's mission is in 'station's orbit (SpaceLevel: the Informer's spy is
        /// theirs then).</summary>
        public static bool TeamHere(int station)
        {
            if (!NetGame.Active || !Freelance.Active || Freelance.Mission.netId == 0) return false;
            foreach (var p in NetPlayer.All)
                if (p != null && !p.IsOwner && p.IsSpawned && p.InSpace && p.Station == station && p.MissionHeld == Freelance.Mission.netId) return true;
            return false;
        }

        /// <summary>SpaceLevel: build this mission's orbit here, unless a squadmate here already runs it.</summary>
        public static bool ShouldRun(int station)
        {
            if (!NetGame.Active || !Freelance.Active || Freelance.Mission.netId == 0) return true;
            long id = Freelance.Mission.netId;
            foreach (var p in NetPlayer.All)
                if (p != null && !p.IsOwner && p.IsSpawned && p.InSpace && p.Station == station && p.MissionRun == id) return false;
            return true;
        }
    }
}
