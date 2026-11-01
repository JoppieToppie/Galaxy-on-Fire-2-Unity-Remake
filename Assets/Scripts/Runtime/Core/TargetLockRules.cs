// TargetLockRules.cs
// The remake's ship lock rules (CombatRadar; CLAUDE.md "NPCs and combat", Radar), plain C# so the pick can be tested:
//   priority   Settings.OriginalTargetLock off (the "Smart" lock): among the ships in the crosshair box the hostile ones
//              come first, then the one nearest the crosshair. The original (Radar::draw) takes the first of the list.
//   hold       Settings.KeepEnemyLock, only with the priority: while a hostile ship is locked and alive no neutral or
//              friendly ship becomes a candidate. Players asked for the two apart: the priority without the hold lets a
//              neutral ship be scanned in a battle.
//   release    the Unlock target control (GameControls.UnlockTarget): the lock is dropped and that ship is no candidate
//              again until it has left the crosshair box (in both lock modes), so the priority doesn't take it straight back
//              and any other ship in the box can be locked.
//   steal      an EMP-disabled ship with cargo in the box is a salvage candidate (the tractor beam's ring), not a ship lock,
//              in either lock mode and whatever ship is held. Multiplayer: another game's NPC (NetProxy) too, from what its
//              owner reports (RemoteStealable); it was only ever a ship lock there, so only the player whose game ran the
//              orbit's NPCs could steal (Discord report 2026-10-21).
//   asteroids  with the Smart lock (Mining.UpdateLock; Discord report 2026-10-24: the original's nearest asteroid in the box
//              locked a rock beside the crosshair, not the one under it): a rock the crosshair's line meets comes first,
//              the front-most of those (the one seen there), even with its centre outside the box; then, of the rocks
//              centred in the box, the one whose edge is nearest the crosshair, the rock already locking keeping a small
//              lead so two side by side don't restart the ring by turns. The Original lock keeps the nearest in 3D.

using UnityEngine;

namespace GoF2Remake.Flight
{
    public static class TargetLockRules
    {
        /// <summary>Added to a non-hostile ship's score: any hostile ship in the box comes first.</summary>
        public const float NonHostilePenalty = 1e9f;

        /// <summary>Whether the lock is held for the enemy: no neutral or friendly candidate meanwhile.</summary>
        public static bool HoldsEnemy(bool priority, bool keepEnemyLock, bool lockedHostileAlive) =>
            priority && keepEnemyLock && lockedHostileAlive;

        /// <summary>Whether a ship in the box may become the candidate.</summary>
        public static bool Eligible(bool holdsEnemy, bool hostile, bool released) => !released && (!holdsEnemy || hostile);

        /// <summary>The priority's score (lower wins): the squared screen distance to the crosshair, hostile ships first.</summary>
        public static float Score(float dx, float dy, bool hostile) => dx * dx + dy * dy + (hostile ? 0f : NonHostilePenalty);

        /// <summary>The released ship stays released while it is alive and in the crosshair box.</summary>
        public static bool StillReleased(bool alive, bool inBox) => alive && inBox;

        /// <summary>Multiplayer: another game's NPC is a steal candidate here while its owner reports it EMP-disabled and
        /// flying with cargo (its first entry: item, amount); a freelance mission ship's cargo only for the mission's team.</summary>
        public static bool RemoteStealable(bool empDisabled, bool flying, bool team, int item, int amount) =>
            empDisabled && flying && team && item >= 0 && amount > 0;

        /// <summary>An asteroid score at or below this: the crosshair's line meets the rock.</summary>
        public const float UnderCrosshair = -1e6f;
        /// <summary>Degrees the asteroid already locking is ahead of the others beside the crosshair.</summary>
        public const float KeepAsteroidDegrees = 0.5f;

        /// <summary>The Smart lock's asteroid score (lower wins; see the header): seen from 'eye' along 'aim' (the line through
        /// the crosshair), a rock of visible radius 'surface' the line meets scores UnderCrosshair + the metres to its
        /// surface; any other the degrees from the line to its edge, less KeepAsteroidDegrees for the current candidate.</summary>
        public static float AsteroidScore(Vector3 eye, Vector3 aim, Vector3 centre, float surface, bool candidate)
        {
            var to = centre - eye;
            float dist = to.magnitude;
            if (dist <= surface) return UnderCrosshair;   // inside the rock
            float off = Vector3.Angle(aim, to), half = Mathf.Asin(surface / dist) * Mathf.Rad2Deg;
            if (off > half) return off - half - (candidate ? KeepAsteroidDegrees : 0f);
            float b = Vector3.Dot(to, aim.normalized);
            return UnderCrosshair + b - Mathf.Sqrt(Mathf.Max(0f, surface * surface - (to.sqrMagnitude - b * b)));
        }

        /// <summary>The score is a rock under the crosshair.</summary>
        public static bool IsUnderCrosshair(float score) => score <= UnderCrosshair / 2f;
    }
}
