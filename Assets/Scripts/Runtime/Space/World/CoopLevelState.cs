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
        /// <summary>The leader's latest radio line shown (-1 = none): a prologue / rescue follower that missed lines (it loaded
        /// later, an RPC before its level was up) catches up to it (Radio.CatchUp), else its cutscene waited for them for good.</summary>
        public int radioLine;
        /// <summary>The level's wormhole (SpaceLevel.Wormhole): shown, its place (the original's coordinates), its timer (ms).
        /// The follower's shows it so (Wormhole.Mirror); its own opened, closed and moved by its own rules (a Discord report
        /// 2026-11-17: the second pilot saw no wormhole at step 40 and the wrong one at 42).</summary>
        public bool holeVisible;
        public Vector3 holePosition;
        public int holeTimer;
        /// <summary>The level's kill count (CampaignLevel.PlayerKills / NpcKills): step 36's wager with Errkt on the follower's
        /// HUD too (a Discord report 2026-11-17: it stayed 0 : 0 for the second pilot).</summary>
        public int playerKills, npcKills;
        /// <summary>The level script's step (CampaignLevel.Event): a follower's scenery plays its moments as the leader's script
        /// reaches them (80 / 81's battlestation, 157's Valkyrie), and the rules that read it (40's wormhole) agree.</summary>
        public int scriptEvent;
        /// <summary>The level's moving scenery (157: the Valkyrie's place, the original's coordinates), if it has one.</summary>
        public bool hasScenery;
        public Vector3 sceneryAt;
    }
}
