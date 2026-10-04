// NetAggression.cs
// Remake multiplayer: the players this player is fighting. Another player is neutral (a yellow marker) until one of the two
// attacks the other with weapons or an EMP: then both are enemies to each other, like an NPC the player shot that turned on
// them (the victim's game counts the attacker's hits, NetPlayer.HitRpc / EmpRpc; the attacker's game its own hits on the
// victim, NetPlayer's RemoteDamage / RemoteEmp; never an NPC's shot, never a squadmate: those can't hit): a red marker,
// and the auto turret, the turrets riding on a debug hull and the deployed sentry guns fire at them
// (PlayerTurret.PickTarget, SentryGun: whatever is hostileToPlayer; NetPlayer sets the flag on their ship each frame).
// An attack, not a stray shot: 3 hits or 50 damage within 10 s (a missile is enough, a bullet or two isn't). It lasts 120 s
// from the last hit either way (every hit starts it again), or until one of the two is destroyed (or the session ends).
// A free for all (/pvp on, an event's Free For All node) makes every other player an enemy at once, squadmates excepted.

using System.Collections.Generic;
using UnityEngine;

namespace GoF2Remake.Multiplayer
{
    [Unity.Scripting.LifecycleManagement.NoAutoStaticsCleanup]
    public static class NetAggression
    {
        public const float HostileSeconds = 120f;
        const int HitsToTurn = 3;
        const float DamageToTurn = 50f, WindowSeconds = 10f;

        sealed class Tally
        {
            public float start, damage;
            public int hits;
        }

        /// <summary>Other player's client id -> until when they are an enemy (unscaled time).</summary>
        static readonly Dictionary<ulong, float> until = new Dictionary<ulong, float>();
        /// <summary>The hits between this player and another one who isn't an enemy yet, in the current 10 s window.</summary>
        static readonly Dictionary<ulong, Tally> tallies = new Dictionary<ulong, Tally>();

        [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.SubsystemRegistration)]
        static void ResetStatics() => Clear();

        /// <summary>A hit between this player and 'other' (either way; 'damage' = its damage, an EMP's amount): an enemy once
        /// it adds up to an attack, and every hit after that keeps them one for another 120 s.</summary>
        public static void Hit(ulong other, float damage)
        {
            float now = Time.unscaledTime;
            if (IsHostile(other)) { until[other] = now + HostileSeconds; return; }
            if (!tallies.TryGetValue(other, out var t) || now - t.start > WindowSeconds) tallies[other] = t = new Tally { start = now };
            t.hits++;
            t.damage += Mathf.Max(0f, damage);
            if (t.hits < HitsToTurn && t.damage < DamageToTurn) return;
            tallies.Remove(other);
            until[other] = now + HostileSeconds;
        }

        /// <summary>'client' is this player's enemy: an attack within the last 120 s, and neither has been destroyed since; or
        /// a free for all (NetState.FreeForAll), where every other player is one but a squadmate.</summary>
        public static bool IsHostile(ulong client)
        {
            if (NetState.FreeForAll)
            {
                var other = NetSquad.Find(client);
                if (other != null && !(NetPlayer.Local != null && NetSquad.Same(NetPlayer.Local, other))) return true;
            }
            return until.TryGetValue(client, out float t) && Time.unscaledTime < t;
        }

        /// <summary>That player was destroyed: no longer an enemy, the hits forgotten.</summary>
        public static void Forget(ulong client)
        {
            until.Remove(client);
            tallies.Remove(client);
        }

        /// <summary>This player died, or the session ended: nobody is an enemy any more.</summary>
        public static void Clear()
        {
            until.Clear();
            tallies.Clear();
        }
    }
}
