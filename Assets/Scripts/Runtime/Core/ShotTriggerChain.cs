// ShotTriggerChain.cs
// Remake (2026-11-06, for mods): the rules of a secure container's shot triggers (World.ShotTrigger, stations.json
// "orbit.containers.secure"), after EVERSPACE 2's BP_ShotTrigger as its compiled script runs it (es2_re kismet):
//   a hit          by the player's weapon lights it (SetTriggered: red flicker -> green, its TriggerSound)
//   ordered        ShotTriggerChainPreviousOne: a hit while the one before is dark puts it out and the ones before it
//                  (OnReset walks the chain back), so they go in order
//   retrigger      Retriggerable: a lit one goes dark again RegtriggerDelay (3 s) after it was lit, so the set is a race
// The container (BP_ItemContainer_Armored, ShotTriggersRequired) unlocks once all are lit at the same time, and stays so.

namespace GoF2Remake.Flight
{
    public sealed class ShotTriggerChain
    {
        /// <summary>BP_ShotTrigger's RegtriggerDelay default (s).</summary>
        public const float DefaultRetriggerS = 3f;

        readonly bool[] lit;
        readonly float[] litMs;
        public readonly bool Ordered;
        /// <summary>Seconds a lit one stays lit (0 = for good).</summary>
        public readonly float RetriggerS;
        public bool Unlocked { get; private set; }
        public int Count => lit.Length;

        public ShotTriggerChain(int count, bool ordered, float retriggerS)
        {
            lit = new bool[System.Math.Max(1, count)];
            litMs = new float[lit.Length];
            Ordered = ordered;
            RetriggerS = retriggerS < 0f ? 0f : retriggerS;
        }

        public bool IsLit(int i) => Unlocked || i >= 0 && i < lit.Length && lit[i];

        /// <summary>The player's shot hit trigger 'i'; true = that unlocked the container.</summary>
        public bool Hit(int i)
        {
            if (Unlocked || i < 0 || i >= lit.Length) return false;
            if (Ordered && i > 0 && !lit[i - 1])
            {
                for (int k = 0; k <= i; k++) lit[k] = false;   // (OnReset: it and the chain before it)
                return false;
            }
            lit[i] = true;
            litMs[i] = 0f;
            foreach (var l in lit) if (!l) return false;
            Unlocked = true;
            return true;
        }

        /// <summary>Time passes: retriggerable ones go dark after RetriggerS.</summary>
        public void Update(float dtMs)
        {
            if (Unlocked || RetriggerS <= 0f) return;
            for (int i = 0; i < lit.Length; i++)
                if (lit[i] && (litMs[i] += dtMs) >= RetriggerS * 1000f) lit[i] = false;
        }
    }
}
