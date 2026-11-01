// CoopLevelState.cs
// Remake co-op: what a follower's story level shows of the leader's (CampaignLevel's leader publishes it a few times a
// second through SpaceHooks.CoopLevelState; NetCoop carries it to the crew in that orbit).

using UnityEngine;

namespace GoF2Remake.World
{
    public struct CoopLevelState
    {
        /// <summary>The orbit and the step the leader's level was built for.</summary>
        public int station, index;
        /// <summary>The level script holds the camera (the follower watches the leader's), the player can't be hurt, the
        /// level waits for a crew member's story conversation (CampaignLevel.Held).</summary>
        public bool cutscene, invulnerable, hold;
        /// <summary>The mission clock and its limit (ms; 0 = none), the route's waypoint.</summary>
        public float missionMs, timeLimitMs;
        public int routeIndex;
        /// <summary>The level's full-screen fade.</summary>
        public float fadeAlpha;
        public Color32 fadeColor;
    }
}
