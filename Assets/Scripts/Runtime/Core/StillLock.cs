// StillLock.cs
// Remake (the user's): whether a locked object holds still, for the fast-forward (multiplayer: the cruise) in manual flight
// toward a stationary lock (Navigation.LockFastForward). A docking target, a ship or an asteroid counts once it has moved
// under UnitsPerMs for HoldMs in a row: a proxy's interpolation jitter stays under that, a ship on its way doesn't.

using UnityEngine;

namespace GoF2Remake.Flight
{
    public class StillLock
    {
        public const float UnitsPerMs = 0.01f, HoldMs = 500f;

        object tracked;
        Vector3 last;
        float heldMs;

        /// <summary>The frame's look at 'obj' at 'units' (game units) after 'dtMs' of game time: true once it has held still
        /// for HoldMs. Another object starts over; no time passing (a pause) changes nothing.</summary>
        public bool Update(object obj, Vector3 units, float dtMs)
        {
            if (obj == null) { Clear(); return false; }
            if (!ReferenceEquals(obj, tracked)) { tracked = obj; heldMs = 0f; }
            else if (dtMs > 0f) heldMs = (units - last).magnitude <= UnitsPerMs * dtMs ? heldMs + dtMs : 0f;
            last = units;
            return heldMs >= HoldMs;
        }

        /// <summary>Nothing locked: the next object starts over.</summary>
        public void Clear() { tracked = null; heldMs = 0f; }
    }
}
