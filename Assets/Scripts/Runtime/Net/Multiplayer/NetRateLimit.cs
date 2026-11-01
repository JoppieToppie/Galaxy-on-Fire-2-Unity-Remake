// NetRateLimit.cs
// Multiplayer, the server: how often each client may send each kind of request (NetState's server RPCs, the hit / shot /
// effect relays of NetPlayer and NetProxy, NetCrate's claims). One token bucket per client and kind: it holds `capacity`
// requests and refills `perSecond`; a request without a token is dropped silently. The limits are well above what real
// play sends (a busy orbit's 40 ships spawning at once, a held shop arrow's 5 units per frame, a battle's shots), so only a
// modified client flooding the server meets them: it is logged once a minute, and a client that keeps flooding (the
// weighted drops of a minute past KickDrops) is kicked at the next sweep (Tick). The host's own player isn't limited (it is
// the server). Validation failures that aren't floods (Reject: a hit from another orbit, a share bigger than the mission)
// are only logged, once a minute per client.

using System.Collections.Generic;
using GoF2Remake.Data;
using Unity.Netcode;
using UnityEngine;

namespace GoF2Remake.Multiplayer
{
    [Unity.Scripting.LifecycleManagement.NoAutoStaticsCleanup]
    public static class NetRateLimit
    {
        public enum Kind
        {
            ProxySpawn, CrateSpawn, Despawn, Adopt, Asteroid, Request, Chat, Command, Invite, Squad, Mission, Trade, Reserve,
            Hangar, Hit, Shot, Fx, Claim, Kill, Siege, PlayerTrade, Pose, Count
        }

        /// <summary>Per kind: the burst a client may send at once, the refill per second, and how much a dropped one counts
        /// toward a kick (the visual relays little: a huge battle may graze their limit).</summary>
        static readonly (float capacity, float perSecond, float kickWeight)[] Limits =
        {
            (200f, 25f, 1f),    // ProxySpawn: entering a busy orbit (traffic, wingmen, a Junk removal's ~80 pieces)
            (80f, 10f, 1f),     // CrateSpawn: a battle's containers
            (200f, 40f, 1f),    // Despawn: ships dying, crates taken
            (200f, 25f, 1f),    // Adopt: an orbit or a mission orbit (its junk too) taken over at once
            (80f, 20f, 1f),     // Asteroid: a nuke through the field
            (20f, 2f, 1f),      // Request: asteroid list, shop stock, hangar snapshot
            (8f, 1f, 1f),       // Chat
            (8f, 1f, 1f),       // Command: the chat's server commands (/w, /players, /kick ...)
            (6f, 0.3f, 1f),     // Invite
            (10f, 1f, 1f),      // Squad: accept, leave
            (40f, 5f, 1f),      // Mission: shares, results, progress
            (400f, 200f, 0.2f), // Trade: a held shop arrow (5 units every 30 ms)
            (10f, 1f, 1f),      // Reserve: dealer ships
            (30f, 3f, 1f),      // Hangar: NPC landings / take-offs, snapshots
            (500f, 250f, 0.1f), // Hit: guns, blasts, EMP on another game's ships
            (800f, 400f, 0.1f), // Shot: every gun's shots and blasts (an orbit authority's whole traffic)
            (12f, 1f, 1f),      // Fx: jump effects, the Khador charge
            (60f, 10f, 1f),     // Claim: crate claims, releases, captures
            (30f, 5f, 1f),      // Kill: kill credits, destroyed-by notices
            (3f, 0.05f, 1f),    // Siege: the Kaamo siege's win
            (30f, 5f, 1f),      // PlayerTrade: a trade between players (offers, accepts, payments)
            (120f, 60f, 0.05f), // Pose: the pose stream's batches (NetPoses: up to 20 a second, a busy orbit's split in a few)
        };

        /// <summary>Weighted drops within a minute that get a client kicked.</summary>
        const float KickDrops = 1500f;
        const float LogSeconds = 60f;

        sealed class Client
        {
            public readonly float[] tokens = new float[(int)Kind.Count];
            public readonly float[] stamp = new float[(int)Kind.Count];
            public float windowStart, weighted, lastLog = -LogSeconds, lastRejectLog = -LogSeconds;
            public int dropped, rejected;
            public Kind lastKind;
            public string lastReject;
            public bool kick;
        }

        static readonly Dictionary<ulong, Client> clients = new Dictionary<ulong, Client>();
        static readonly List<ulong> kicks = new List<ulong>();

        /// <summary>A new session: nobody has sent anything yet.</summary>
        public static void Reset()
        {
            clients.Clear();
            kicks.Clear();
        }

        /// <summary>A client left: its buckets go.</summary>
        public static void Forget(ulong client) => clients.Remove(client);

        static bool Exempt(ulong client)
        {
            var m = NetworkManager.Singleton;
            return client == NetworkManager.ServerClientId && m != null && m.IsHost;   // the host's own player is the server
        }

        static Client Get(ulong client)
        {
            if (clients.TryGetValue(client, out var c)) return c;
            c = new Client { windowStart = Time.unscaledTime };
            float now = Time.unscaledTime;
            for (int i = 0; i < (int)Kind.Count; i++) { c.tokens[i] = Limits[i].capacity; c.stamp[i] = now; }
            clients[client] = c;
            return c;
        }

        /// <summary>Server: may 'client' send one more request of 'kind' now? False = drop it (counted, logged once a minute).</summary>
        public static bool Allow(ulong client, Kind kind)
        {
            if (Exempt(client)) return true;
            var c = Get(client);
            if (c.kick) return false;   // on the way out
            int k = (int)kind;
            float now = Time.unscaledTime;
            var limit = Limits[k];
            c.tokens[k] = Mathf.Min(limit.capacity, c.tokens[k] + (now - c.stamp[k]) * limit.perSecond);
            c.stamp[k] = now;
            if (c.tokens[k] >= 1f) { c.tokens[k] -= 1f; return true; }
            // Over the limit: dropped. A client that keeps it up for a minute is kicked (Tick).
            if (now - c.windowStart > 60f) { c.windowStart = now; c.weighted = 0f; }
            c.weighted += limit.kickWeight;
            c.dropped++;
            c.lastKind = kind;
            if (now - c.lastLog >= LogSeconds)
            {
                Debug.LogWarning($"NetRateLimit: {Who(client)} is over the {kind} limit ({c.dropped} request(s) dropped since the last report).");
                c.lastLog = now;
                c.dropped = 0;
            }
            if (c.weighted >= KickDrops && !c.kick) { c.kick = true; kicks.Add(client); }
            return false;
        }

        /// <summary>Server: a request from 'client' failed a check (not a flood): logged once a minute per client.</summary>
        public static void Reject(ulong client, string what)
        {
            if (Exempt(client)) return;
            var c = Get(client);
            float now = Time.unscaledTime;
            c.rejected++;
            c.lastReject = what;
            if (now - c.lastRejectLog < LogSeconds) return;
            Debug.LogWarning($"NetRateLimit: refused {Who(client)}'s request: {what} ({c.rejected} refused since the last report).");
            c.lastRejectLog = now;
            c.rejected = 0;
        }

        /// <summary>Server, NetState's sweep: the clients that kept flooding are kicked (not inside an RPC's handling).</summary>
        public static void Tick()
        {
            if (kicks.Count == 0) return;
            var now = kicks.ToArray();
            kicks.Clear();
            foreach (var client in now)
            {
                Debug.LogWarning($"NetRateLimit: kicking {Who(client)}: it kept flooding the server ({(clients.TryGetValue(client, out var c) ? c.lastKind.ToString() : "?")}).");
                NetGame.Kick(client, Localization.Extra("mpKickedFlood", "You were removed from the session: your game sent too many requests."));
                if (NetState.Instance != null && NetState.Instance.IsServer)
                {
                    var p = NetSquad.Find(client);
                    if (p != null) NetState.Instance.NoticeAll(string.Format(Localization.Extra("mpKickedFloodNotice",
                        "{0} was removed from the session (too many requests)."), p.DisplayName));
                }
            }
        }

        static string Who(ulong client)
        {
            var p = NetSquad.Find(client);
            return p != null ? $"client {client} ({p.DisplayName})" : $"client {client}";
        }
    }
}
