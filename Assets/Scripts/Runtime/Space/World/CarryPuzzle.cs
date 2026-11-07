// CarryPuzzle.cs
// Remake (2026-11-06, for mods): the action key's part in the secure containers' energy sockets and conductors (World.Portable,
// World.OrbDispenser, World.EnergySocket, World.ConductorNet), for the flight HUD's prompt chain after the containers: carrying something,
// "DROP" lets it go (ES2: the action key again); else a loose sphere or coil within reach in front, "GRAB"; else a ready
// dispenser within reach, "GRAB" hands a new sphere. Reach: the containers' (LootContainer.OpenRangeM, in front). While
// carrying, primary fire throws it instead of shooting (Throw; the HUD holds the guns back, WeaponSystem.PrimaryBlocked).

using GoF2Remake.Data;
using UnityEngine;

namespace GoF2Remake.World
{
    public static class CarryPuzzle
    {
        /// <summary>What the ship carries, null = nothing.</summary>
        public static Portable Carried(Transform ship)
        {
            if (ship == null) return null;
            foreach (var p in Portable.All) if (p != null && p.Carrier == ship) return p;
            return null;
        }

        static T Nearest<T>(Transform ship, System.Collections.Generic.IEnumerable<T> all, System.Func<T, bool> ok) where T : Component
        {
            T best = null;
            float bestD = LootContainer.OpenRangeM * LootContainer.OpenRangeM;
            foreach (var c in all)
            {
                if (c == null || !ok(c)) continue;
                var d = c.transform.position - ship.position;
                if (d.sqrMagnitude > bestD || Vector3.Dot(d, ship.forward) <= 0f) continue;
                best = c;
                bestD = d.sqrMagnitude;
            }
            return best;
        }

        /// <summary>The action prompt, null = nothing to do.</summary>
        public static string Prompt(Transform ship)
        {
            if (ship == null) return null;
            if (Carried(ship) != null) return Localization.Extra("hudDrop", "DROP");
            if (Nearest(ship, Portable.All, p => !p.Socketed && p.Carrier == null) != null || Nearest(ship, OrbDispenser.All, d => d.Ready) != null)
                return Localization.Extra("hudGrab", "GRAB");
            return null;
        }

        /// <summary>Throws what the ship carries along its nose (Portable.Release fling); true = there was something.</summary>
        public static bool Throw(Transform ship)
        {
            var carried = Carried(ship);
            if (carried == null) return false;
            carried.Release(true);
            return true;
        }

        /// <summary>The action key: drop, grab or dispense; true = something happened.</summary>
        public static bool Act(Transform ship)
        {
            if (ship == null) return false;
            var carried = Carried(ship);
            if (carried != null) { carried.Release(); return true; }
            var loose = Nearest(ship, Portable.All, p => !p.Socketed && p.Carrier == null);
            if (loose != null) { loose.Grab(ship); return true; }
            var disp = Nearest(ship, OrbDispenser.All, d => d.Ready);
            return disp != null && disp.Dispense(ship) != null;
        }
    }
}
