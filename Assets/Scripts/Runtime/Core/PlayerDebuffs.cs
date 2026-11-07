// PlayerDebuffs.cs
// Remake (2026-11-04, for mods): the debuffs on the player's ship, for EVERSPACE 2's System Recovery Routine ("removes most
// debuffs and resets any debuff buildups"; Flight.Device.Type.Recovery). Whatever puts one on the ship registers how to tell
// it is on and how to lift it: the web's slow (ShipController), a rift's corrosion (World.RiftField).

using System;
using System.Collections.Generic;

namespace GoF2Remake.Flight
{
    public static class PlayerDebuffs
    {
        static readonly List<(Func<bool> on, Action lift)> all = new List<(Func<bool>, Action)>();

        public static void Register(Func<bool> on, Action lift) { if (on != null && lift != null) all.Add((on, lift)); }

        public static void Unregister(Func<bool> on) => all.RemoveAll(d => d.on == on);

        /// <summary>Whether any debuff is on the ship now.</summary>
        public static bool Any
        {
            get { foreach (var d in all) if (d.on()) return true; return false; }
        }

        /// <summary>Every debuff lifted.</summary>
        public static void Lift()
        {
            foreach (var d in all.ToArray()) if (d.on()) d.lift();
        }
    }
}
