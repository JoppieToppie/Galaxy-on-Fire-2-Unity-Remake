// PointDefense.cs
// Remake, for mods (EVERSPACE 2's Missile Defense System; the original has no point defense): equipment with the stat
// "missileDefense" (attr 131, a reach in game units) lets the ship's auto turrets shoot down incoming homing missiles,
// rockets and nukes within its reach. "missileDefenseTargets" (attr 144) selects which: bits 1/2/4 respectively, with
// zero (the default) covering all three. While covered ordnance is within reach, an auto turret (PlayerTurret) turns to it
// instead of its ship target, leading it by the turret's shot's travel time, and fires when aligned; each shot destroys it
// when the shot arrives (that travel time later), with the rocket impact's burst (GunRig.ShowImpact's
// SET_EXPLOSION_MANUALLY_BIG). An ordnance projectile is claimed by one shot only, so several turrets spread over a salvo.
// The projectiles are the ones IncomingMissiles reports:
// those homing on the local player that still hold their lock. Multiplayer: another game's missile drawn here (Gun.Mirror)
// is left alone: its real copy flies in the shooter's game, which applies its hit.
// Modified (review of #168): a claim holds the missile's age when it was made, so a new missile fired from the same bullet
// slot before the claim ran out is a missile of its own (neither skipped by Pick nor shot down by the old claim); the claims
// go with the scene (SceneManager.sceneUnloaded), so no burst from a level left behind comes up in the next one.

using System.Collections.Generic;
using GoF2Remake.Data;
using UnityEngine;
using UnityEngine.SceneManagement;

namespace GoF2Remake.Flight
{
    [Unity.Scripting.LifecycleManagement.NoAutoStaticsCleanup]
    public static class PointDefense
    {
        /// <summary>The item attribute (Modding.ItemStats "missileDefense").</summary>
        public const int Attribute = 131;
        /// <summary>Optional mode stat: 0 = balanced, 1 = pure missile defense.</summary>
        public const int ModeAttribute = 143;
        /// <summary>Optional coverage bitmask: 1 homing missiles, 2 rockets, 4 nukes; 0 = all three.</summary>
        public const int TargetsAttribute = 144;
        public const int HomingMissileTarget = 1, RocketTarget = 2, NukeTarget = 4;
        const int AllTargets = HomingMissileTarget | RocketTarget | NukeTarget;

        static readonly List<IncomingMissiles.Missile> incoming = new List<IncomingMissiles.Missile>();
        // -> when it dies (Time.time) and the missile's age when claimed (Gun.Bullet.age: a younger one is a new missile)
        static readonly Dictionary<(Gun, int), (float dueAt, float age)> claimed = new Dictionary<(Gun, int), (float, float)>();
        static readonly List<(Gun, int)> due = new List<(Gun, int)>();
        static int collectedFrame = -1, tickedFrame = -1;

        /// <summary>The burst where a missile is shot down (the rocket impact's); tests set their own.</summary>
        public static System.Action<Vector3> Exploded = at => ShipBurn.ManualBurst(at, 1.5f, 2000f, 3000f, 500f);

        [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.SubsystemRegistration)]
        public static void ResetStatics()
        {
            Clear();
            SceneManager.sceneUnloaded -= OnSceneUnloaded;
            SceneManager.sceneUnloaded += OnSceneUnloaded;
        }

        static void OnSceneUnloaded(Scene _) => Clear();

        /// <summary>No missiles, no claims (a level left: its guns are gone).</summary>
        public static void Clear() { incoming.Clear(); claimed.Clear(); due.Clear(); collectedFrame = tickedFrame = -1; }

        /// <summary>A claim on 'key' that is still on the missile it was made for (not a newer one in its bullet slot).</summary>
        static bool Claimed((Gun gun, int index) key)
        {
            if (!claimed.TryGetValue(key, out var c)) return false;
            if (key.gun.bullets[key.index].age >= c.age) return true;
            claimed.Remove(key);   // the claimed one is gone, its slot fired again
            return false;
        }

        /// <summary>The strongest mounted missile-defense mode: 0 = balanced, 1 = reserve auto turrets for ordnance.</summary>
        public static int Mode(Database db)
        {
            if (db == null) return 0;
            int mode = 0;
            foreach (var e in Session.Equipment)
            {
                var it = db.Item(e.item);
                if (it != null && it.Attr(Attribute) > 0)
                    mode = Mathf.Max(mode, Mathf.Clamp(it.Attr(ModeAttribute), 0, 1));
            }
            return mode;
        }

        /// <summary>The union of mounted defense equipment's ordnance coverage; legacy items (0) cover all types.</summary>
        public static int TargetMask(Database db)
        {
            if (db == null) return 0;
            int mask = 0;
            foreach (var e in Session.Equipment)
            {
                var it = db.Item(e.item);
                if (it == null || it.Attr(Attribute) <= 0) continue;
                int configured = it.Attr(TargetsAttribute);
                mask |= configured == 0 ? AllTargets : configured & AllTargets;
            }
            return mask;
        }

        /// <summary>The reach of the mounted equipment's missile defense in game units (the longest), 0 = none.</summary>
        public static float ReachUnits(Database db)
        {
            if (db == null) return 0f;
            int best = 0;
            foreach (var e in Session.Equipment)
            {
                var it = db.Item(e.item);
                if (it != null) best = Mathf.Max(best, it.Attr(Attribute));
            }
            return best;
        }

        /// <summary>The nearest covered projectile within reach of 'from' that no shot is on yet. False = none.</summary>
        public static bool Pick(Vector3 from, float reachMeters, int targetMask, out IncomingMissiles.Missile missile)
        {
            if (collectedFrame != Time.frameCount) { IncomingMissiles.Collect(incoming); collectedFrame = Time.frameCount; }
            missile = default;
            float best = reachMeters * reachMeters;
            bool found = false;
            foreach (var m in incoming)
            {
                if (m.gun == null || m.gun.Mirror || !m.gun.IsActive(m.index) || Claimed((m.gun, m.index))) continue;
                int type = m.gun.Homing ? HomingMissileTarget :
                    m.gun.kind == Gun.Kind.Rocket ? RocketTarget :
                    m.gun.kind == Gun.Kind.Nuke ? NukeTarget : 0;
                if (type == 0 || (targetMask & type) == 0) continue;
                float d = (m.position - from).sqrMagnitude;
                if (d > best) continue;
                best = d; missile = m; found = true;
            }
            return found;
        }

        /// <summary>Where to aim at 'm' for a shot of 'shotSpeed' metres per ms from 'from', and the shot's flight time (ms).</summary>
        public static Vector3 Lead(IncomingMissiles.Missile m, Vector3 from, float shotSpeed, out float flightMs)
        {
            flightMs = shotSpeed > 1e-5f ? Vector3.Distance(m.position, from) / shotSpeed : 0f;
            var at = m.position + m.velocity * flightMs;   // one refinement: the time to where it will be
            flightMs = shotSpeed > 1e-5f ? Vector3.Distance(at, from) / shotSpeed : 0f;
            return m.position + m.velocity * flightMs;
        }

        /// <summary>A shot is on its way to 'm': it is destroyed 'flightMs' from now.</summary>
        public static void Claim(IncomingMissiles.Missile m, float flightMs)
        {
            if (m.gun != null) claimed[(m.gun, m.index)] = (Time.time + flightMs / 1000f, m.gun.bullets[m.index].age);
        }

        /// <summary>The claimed missiles whose shot has arrived go (once a frame, whichever turret calls first).</summary>
        public static void Tick()
        {
            if (tickedFrame == Time.frameCount || claimed.Count == 0) return;
            tickedFrame = Time.frameCount;
            due.Clear();
            foreach (var kv in claimed) if (Time.time >= kv.Value.dueAt) due.Add(kv.Key);
            foreach (var key in due)
            {
                bool same = Claimed(key);   // (false: a newer missile flies in its slot)
                claimed.Remove(key);
                var (gun, i) = key;
                if (!same || !gun.IsActive(i)) continue;   // it hit, ran out or was shaken off meanwhile
                var at = gun.ShootDown(i);
                Exploded?.Invoke(at);
            }
        }
    }
}
