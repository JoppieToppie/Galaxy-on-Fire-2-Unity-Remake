// NetShotRegistry.cs
// Server-side identity for in-flight shots. Defense requests can only cancel a shot routed to that defender;
// later damage reports for a canceled shot are discarded.
using System.Collections.Generic;
using GoF2Remake.Data;
using GoF2Remake.Flight;
using UnityEngine;

namespace GoF2Remake.Multiplayer
{
    public static class NetShotRegistry
    {
        struct Shot { public ulong target; public float expiresAt; public bool canceled, interceptable; }
        static readonly Dictionary<(ulong source, uint id), Shot> shots = new Dictionary<(ulong, uint), Shot>();
        static readonly List<(ulong source, uint id)> expired = new List<(ulong, uint)>();

        [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.SubsystemRegistration)]
        static void Reset() { shots.Clear(); expired.Clear(); }
        public static void Register(ulong source, uint id, int item, ulong target, float lifetimeMs)
        {
            if (id == 0 || target == NetShots.NoTarget) return;
            var weapon = (NetGame.Db ?? Database.Shared)?.Item(item);
            bool interceptable = weapon != null && (weapon.Attr(105) > 0 ||
                (Gun.Kind)weapon.categoryId is Gun.Kind.Rocket or Gun.Kind.Missile or Gun.Kind.Nuke);
            Prune();
            shots[(source, id)] = new Shot { target = target, expiresAt = Time.time + Mathf.Max(0f, lifetimeMs) / 1000f, interceptable = interceptable };
        }
        public static bool TryCancel(ulong source, uint id, ulong defender)
        {
            Prune();
            if (!shots.TryGetValue((source, id), out var shot) || shot.canceled || !shot.interceptable || shot.target != defender) return false;
            shot.canceled = true;
            shots[(source, id)] = shot;
            return true;
        }
        public static bool IsCanceled(ulong source, uint id)
        {
            Prune();
            return id != 0 && shots.TryGetValue((source, id), out var shot) && shot.canceled;
        }
        static void Prune()
        {
            expired.Clear();
            foreach (var pair in shots) if (Time.time >= pair.Value.expiresAt) expired.Add(pair.Key);
            foreach (var key in expired) shots.Remove(key);
        }
    }
}
