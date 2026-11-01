// NetHandOverRule.cs
// Which orbit objects the server hands to the orbit's runner (NetState.HandOver, docs/notes/multiplayer-sync.md "Orbit
// authority"), as a plain rule the edit-mode tests check: every object whose game no longer flies in the orbit goes to the
// runner, whatever kind it is (a ship, a fixed object, a turret, a capital ship's part, junk, a wreck, a dying ship, a
// crate). Kept: one its game still runs there. Waiting: a squad mission's ship while a squadmate of its creator is there
// (FreelanceOrbit's promotion takes it with its role) for a few seconds. Left to the sweep: a player's wingman (it goes
// with its player) and an object nobody could rebuild.

namespace GoF2Remake.Multiplayer
{
    public static class NetHandOverRule
    {
        public enum Act { Keep, Wait, HandOver, Sweep }

        /// <summary>The seconds a squad mission's ship waits for a squadmate's promotion before the runner gets it.</summary>
        public const float MissionWaitSeconds = 6f;

        /// <param name="ownerHere">its owner's game flies in its orbit</param>
        /// <param name="crate">a NetCrate (its holder takes its own copy over)</param>
        /// <param name="inherited">a disconnected player's object the host holds (no ship behind it in the host's game)</param>
        /// <param name="wingman">a player's hired wingman</param>
        /// <param name="rebuildable">its description can be rebuilt (NetProxy.HandOverable)</param>
        /// <param name="missionWithMateHere">a squad mission's ship or junk with a squadmate of its creator in the orbit</param>
        /// <param name="waitedSeconds">how long it has waited for that</param>
        public static Act Decide(bool ownerHere, bool crate, bool inherited, bool wingman, bool rebuildable, bool missionWithMateHere, float waitedSeconds)
        {
            if (ownerHere && (crate || !inherited)) return Act.Keep;
            if (!crate && (wingman || !rebuildable)) return Act.Sweep;
            if (!crate && missionWithMateHere && waitedSeconds < MissionWaitSeconds) return Act.Wait;
            return Act.HandOver;
        }
    }
}
