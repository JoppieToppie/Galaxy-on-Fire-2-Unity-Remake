// NetSquad.cs
// Multiplayer squads. The host keeps them (NetPlayer's squad id, NetState's RPCs): a player invites another from the
// hangar's pilot list (SquadView), the invited player accepts or declines in a popup; accepting joins the inviter's
// squad (a new one if the inviter had none; leaving the old one); Leave (the squad window) leaves it; a squad of one is
// dissolved. Squadmates are green markers, and neither player's weapons affect the other (Target.playerProof); NPCs a
// squadmate made hostile are hostile to the whole squad (NpcShip.aggressors, NetOrbit). Outside a squad every other
// player is neutral (yellow): they can hit and destroy each other.

using System;
using System.Collections.Generic;
using GoF2Remake.Data;
using UnityEngine;

namespace GoF2Remake.Multiplayer
{
    [Unity.Scripting.LifecycleManagement.NoAutoStaticsCleanup]
    public static class NetSquad
    {
        public const float InviteSeconds = 45f;
        public const int MaxMembers = 6;
        public static int MemberCount(NetPlayer pilot)
        {
            if (pilot == null) return 0;
            if (pilot.SquadId == 0) return 1;
            int count = 0;
            foreach (var p in NetPlayer.All) if (p != null && p.IsSpawned && p.SquadId == pilot.SquadId) count++;
            return count;
        }
        public static bool IsFull(NetPlayer pilot) => MemberCount(pilot) >= MaxMembers;

        public sealed class Invite
        {
            public ulong from;
            public string name;
            public float time;
        }

        static readonly List<Invite> invites = new List<Invite>();
        static readonly Dictionary<ulong, float> invited = new Dictionary<ulong, float>();

        /// <summary>The invitations waiting for an answer (newest last), expired ones dropped.</summary>
        public static IReadOnlyList<Invite> Invites
        {
            get
            {
                // Expired, gone, or from a squadmate already (an old invitation: accepting it would drop the squad's mission).
                invites.RemoveAll(i => Time.unscaledTime - i.time > InviteSeconds || Find(i.from) == null || Same(Find(i.from), NetPlayer.Local));
                return invites;
            }
        }

        public static int LocalSquad => NetPlayer.Local != null ? NetPlayer.Local.SquadId : 0;
        public static bool InSquad => LocalSquad != 0;

        public static NetPlayer Find(ulong client)
        {
            foreach (var p in NetPlayer.All) if (p != null && p.IsSpawned && p.OwnerClientId == client) return p;
            return null;
        }

        /// <summary>The same player, or both in one squad.</summary>
        public static bool Same(NetPlayer a, NetPlayer b) => a != null && b != null && (a == b || (a.SquadId != 0 && a.SquadId == b.SquadId));

        /// <summary>Client 'client' is 'p' or in p's squad.</summary>
        public static bool SameClient(ulong client, NetPlayer p) => p != null && (client == p.OwnerClientId || Same(Find(client), p));

        /// <summary>The local player's squad, the local player included (empty outside one).</summary>
        public static List<NetPlayer> Members()
        {
            var list = new List<NetPlayer>();
            int id = LocalSquad;
            if (id == 0) return list;
            foreach (var p in NetPlayer.All) if (p != null && p.IsSpawned && p.SquadId == id) list.Add(p);
            return list;
        }

        /// <summary>An invitation was sent to 'p' in the last InviteSeconds (the pilot list shows "Invited").</summary>
        public static bool WasInvited(NetPlayer p) => p != null && invited.TryGetValue(p.OwnerClientId, out float t) && Time.unscaledTime - t < InviteSeconds;

        public static void InviteTo(NetPlayer p)
        {
            if (p == null || NetState.Instance == null || !NetState.Instance.IsSpawned) return;
            if (IsFull(NetPlayer.Local)) { NetChat.Notice("Your squad is full (6/6)."); return; }
            invited[p.OwnerClientId] = Time.unscaledTime;
            NetState.Instance.InviteRpc(p.OwnerClientId);
        }

        internal static void OnInvited(ulong from, string name)
        {
            invites.RemoveAll(i => i.from == from);
            invites.Add(new Invite { from = from, name = name, time = Time.unscaledTime });
            NetChat.Notice(string.Format(Localization.Extra("mpSquadInvited", "{0} invites you to their squad."), name));
        }

        public static void Accept(Invite invite)
        {
            invites.Remove(invite);
            if (NetState.Instance != null && NetState.Instance.IsSpawned) NetState.Instance.AcceptInviteRpc(invite.from);
        }

        public static void Decline(Invite invite) => invites.Remove(invite);

        public static void Leave()
        {
            if (NetState.Instance != null && NetState.Instance.IsSpawned) NetState.Instance.LeaveSquadRpc();
        }

        public static void Clear()
        {
            invites.Clear();
            invited.Clear();
        }
    }
}
