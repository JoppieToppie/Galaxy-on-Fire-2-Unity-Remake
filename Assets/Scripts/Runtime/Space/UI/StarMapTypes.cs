// StarMapTypes.cs
// The star map's modes and result, which the flight level (SystemJump) passes through SpaceHooks.OpenStarMap: the map
// itself (StarMap) is in GoF2.App.

namespace GoF2Remake.UI
{
    /// <summary>Mission = StarMap(true, mission, ...): centred on a mission's target; view only in the original (remake: the
    /// Missions window's map travels like the station's, StarMap.Open's allowTravel).</summary>
    public enum StarMapMode { Station, Gate, Khador, Mission }

    /// <summary>What the player picked: station -1 = closed without a destination.</summary>
    public struct StarMapResult
    {
        public int station;
        public bool instantJump;   // Khador jump to another system
        public int cells;          // energy cells for that jump
        public bool toVoid;        // 422 answered Yes: the Khador jump into the Void (station is -1 then)
        public bool safestRoute;   // remake: a gate route round the dangerous systems, not the fewest jumps (GalaxyMap.RoutePath)
    }
}
