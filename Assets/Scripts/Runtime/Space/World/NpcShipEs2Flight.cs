// NpcShipEs2Flight.cs
// NpcShip, part: EVERSPACE 2's physics flight (remake option Settings.Es2NpcFlight, a session rule; Flight.Es2NpcFlight the
// rules). Steer asks Es2FlightTurn for the new heading instead of GoF2's nudge (turn rates that ramp up and coast out of a
// turn) and Es2FlightMove for the move instead of GoF2's constant forward speed (a velocity that accelerates, brakes and
// drifts a little through turns). Every 2 s (staggered) the path test looks 4 s ahead toward the wanted direction and, when
// that would run into an asteroid, a station, a gate, a freighter or another of the orbit's obstacles, toward ES2's other
// candidates until one is clear (its target's own obstacle aside). Every NPC ship Steer moves (fighters, wingmen, drones),
// not freighters, turrets, fixed objects, capital ships or an event graph's orders, which keep GoF2's flight.

using System.Collections.Generic;
using GoF2Remake.Data;
using GoF2Remake.Flight;
using UnityEngine;

namespace GoF2Remake.World
{
    public partial class NpcShip
    {
        /// <summary>A path test also when the wanted direction turned this far (degrees) from the last test's.</summary>
        const float Es2RetestDeg = 30f;
        const int Es2AsteroidRefreshFrames = 30;

        static readonly List<Target> es2Asteroids = new List<Target>();
        static readonly List<Target> es2Near = new List<Target>();
        static readonly Vector3[] es2Dirs = new Vector3[Es2NpcFlight.TestDirectionCount];
        static int es2AsteroidFrame = -1000;

        [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.SubsystemRegistration)]
        static void ResetEs2Flight() { es2Asteroids.Clear(); es2Near.Clear(); es2AsteroidFrame = -1000; }

        void SetupEs2Flight(SpawnSpec spec)
        {
            es2Flight = Settings.Es2NpcFlight && !spec.freighter && spec.fixedObject == null && spec.turretAssembly == null
                        && spec.capital == 0 && !spec.capitalPart && !spec.modCapital;
            es2FlownFrame = -2;
            es2PathMs = Random.Range(0f, Es2NpcFlight.ForceUpdateS * 1000f);   // (staggered)
        }

        /// <summary>The new forward for this frame on ES2's turn rules, toward 'wanted' (unit, world) or the path test's
        /// detour. 'top' = the speed it flies at (units a ms), for the path test.</summary>
        Vector3 Es2FlightTurn(Vector3 wanted, float top, float dtMs)
        {
            Es2SyncFlight(top);
            float dt = dtMs / 1000f;
            if ((es2PathMs -= dtMs) <= 0f || Vector3.Angle(wanted, es2PathWanted) > Es2RetestDeg)
            {
                es2PathMs = Es2NpcFlight.ForceUpdateS * 1000f;
                es2PathWanted = wanted;
                Es2PathTest(wanted, top);
            }
            var go = es2PathStraight ? wanted : es2PathDir;
            var local = Quaternion.Inverse(transform.rotation) * go;
            float yawErr = Mathf.Atan2(local.x, local.z) * Mathf.Rad2Deg;                                   // + right
            float pitchErr = -Mathf.Atan2(local.y, new Vector2(local.x, local.z).magnitude) * Mathf.Rad2Deg;  // + nose down
            float topRate = Es2NpcFlight.TopTurnDegS, accel = Es2NpcFlight.TurnAccelDegS2;
            es2YawRate = Es2NpcFlight.Ramp(es2YawRate, Es2NpcFlight.RotationInput(yawErr, es2YawRate, accel) * topRate, accel, dt);
            es2PitchRate = Es2NpcFlight.Ramp(es2PitchRate, Es2NpcFlight.RotationInput(pitchErr, es2PitchRate, accel) * topRate, accel, dt);
            return transform.rotation * Quaternion.Euler(es2PitchRate * dt, es2YawRate * dt, 0f) * Vector3.forward;
        }

        /// <summary>This frame's move (Unity metres) at the top speed 'top' (units a ms): thrust eased toward it, the brake
        /// above it, the velocity swung toward the nose.</summary>
        Vector3 Es2FlightMove(float top, float dtMs)
        {
            Es2SyncFlight(top);
            es2FlownFrame = Time.frameCount;
            float dt = dtMs / 1000f;
            float topS = top * 1000f * es2PathSpeed;   // units a second
            float accel = Es2NpcFlight.AccelPerS * top * 1000f;
            var fwd = transform.forward;
            var v = es2Vel;
            float spd = v.magnitude;
            if (spd > 1e-3f)
            {
                float pull = Mathf.Clamp01((Vector3.Dot(v / spd, fwd) + 1f) * 0.5f * dt * Es2NpcFlight.AccelCm / Es2NpcFlight.TurningBoostCm);
                v += (fwd * spd - v) * pull;
            }
            spd = v.magnitude;
            if (spd > topS * 1.01f) v *= Mathf.Max(topS, spd - accel * Es2NpcFlight.BrakeShare * dt) / spd;
            float cap = Mathf.Max(topS, v.magnitude);
            v += fwd * (accel * Es2NpcFlight.Ease(Vector3.Dot(v, fwd), topS) * dt);
            if (v.sqrMagnitude > cap * cap) v = v.normalized * cap;
            es2Vel = v;
            return v * (dt * M);
        }

        /// <summary>Coming from GoF2's flight (spawned, an order, an EMP): the velocity and rates pick up from there.</summary>
        void Es2SyncFlight(float top)
        {
            if (es2FlownFrame >= Time.frameCount - 1) return;
            es2Vel = transform.forward * (top * 1000f);
            es2YawRate = es2PitchRate = 0f;
            es2PathStraight = true;
            es2PathSpeed = 1f;
            es2FlownFrame = Time.frameCount;
        }

        /// <summary>GenerateNextWaypoint: the wanted direction if its path is clear, else the first clear candidate of ES2's
        /// table (TestDirectionCount round each cone, nearest the current heading first), else the wanted one anyway.</summary>
        void Es2PathTest(Vector3 wanted, float top)
        {
            es2PathStraight = true;
            es2PathSpeed = 1f;
            float r = Mathf.Max(1f, Target != null ? Target.radius : 10f);
            float reach = top * 1000f * Es2NpcFlight.PredictionS * M + r;
            Es2GatherAsteroids(transform.position, reach);
            if (es2Near.Count == 0 && !Es2ObstacleNear(transform.position, reach)) return;   // nothing to run into
            var side = Vector3.Cross(wanted, Mathf.Abs(Vector3.Dot(wanted, Vector3.up)) > 0.95f ? Vector3.right : Vector3.up).normalized;
            var fwd = transform.forward;
            var dirs = es2Dirs;
            foreach (var (cone, speedFactor) in Es2NpcFlight.Candidates)
            {
                int n = cone < 0.01f ? 1 : dirs.Length;
                for (int k = 0; k < n; k++)
                {
                    var axis = Quaternion.AngleAxis(k * 360f / n, wanted) * side;
                    var d = Quaternion.AngleAxis(cone, axis) * wanted;
                    int j = k;   // (insertion: nearest the current heading first)
                    while (j > 0 && Vector3.Dot(dirs[j - 1], fwd) < Vector3.Dot(d, fwd)) { dirs[j] = dirs[j - 1]; j--; }
                    dirs[j] = d;
                }
                for (int k = 0; k < n; k++)
                {
                    if (!Es2PathClear(dirs[k], top * speedFactor, r)) continue;
                    if (cone < 0.01f && speedFactor >= 1f) return;   // straight on
                    es2PathStraight = false;
                    es2PathDir = dirs[k];
                    es2PathSpeed = speedFactor;
                    return;
                }
            }
        }

        /// <summary>TestPathByEulerIntegration: its flight toward 'dir' at 'speed' (units a ms), turning at GoF2's top rate
        /// after ES2's ramp, sampled PredictionS ahead against the near asteroids and the obstacles.</summary>
        bool Es2PathClear(Vector3 dir, float speed, float r)
        {
            var p = transform.position;
            var f = transform.forward;
            float rate = Mathf.Sqrt(es2YawRate * es2YawRate + es2PitchRate * es2PitchRate);
            float dt = 1f / Es2NpcFlight.SamplesPerS;
            int n = Mathf.CeilToInt(Es2NpcFlight.PredictionS * Es2NpcFlight.SamplesPerS);
            int frame = Time.frameCount;
            for (int i = 0; i < n; i++)
            {
                rate = Es2NpcFlight.Ramp(rate, Es2NpcFlight.TopTurnDegS, Es2NpcFlight.TurnAccelDegS2, dt);
                f = Vector3.RotateTowards(f, dir, rate * dt * Mathf.Deg2Rad, 0f);
                p += f * (speed * 1000f * dt * M);
                foreach (var a in es2Near)
                {
                    float rr = a.radius + r;
                    if ((a.transform.position - p).sqrMagnitude < rr * rr) return false;
                }
                var all = Obstacle.All;
                for (int k = 0; k < all.Count; k++)
                {
                    var o = all[k];
                    if ((object)o == null || (target != null && o.transform == target.transform) || !o.MayTouch(p, frame)) continue;
                    if (o.Touches(p, out _)) return false;
                }
            }
            return true;
        }

        /// <summary>The living asteroids within 'reach' of 'pos' into es2Near (the orbit's list refreshed every 30 frames).</summary>
        static void Es2GatherAsteroids(Vector3 pos, float reach)
        {
            if (Time.frameCount - es2AsteroidFrame >= Es2AsteroidRefreshFrames)
            {
                es2AsteroidFrame = Time.frameCount;
                es2Asteroids.Clear();
                foreach (var t in Target.All) if (t != null && t.isAsteroid) es2Asteroids.Add(t);
            }
            es2Near.Clear();
            foreach (var a in es2Asteroids)
            {
                if (a == null || !a.Alive || !a.isActiveAndEnabled) continue;
                float rr = reach + a.radius;
                if ((a.transform.position - pos).sqrMagnitude < rr * rr) es2Near.Add(a);
            }
        }

        /// <summary>Any of the orbit's obstacles may lie within 'reach' (a few points toward each side tried on their broad sphere).</summary>
        bool Es2ObstacleNear(Vector3 pos, float reach)
        {
            int frame = Time.frameCount;
            var all = Obstacle.All;
            for (int k = 0; k < all.Count; k++)
            {
                var o = all[k];
                if ((object)o == null || (target != null && o.transform == target.transform)) continue;
                var c = o.transform.position;
                var toward = c - pos;
                float d = toward.magnitude;
                var probe = d > reach ? pos + toward / d * reach : c;   // the nearest point of our reach toward it
                if (o.MayTouch(probe, frame)) return true;
            }
            return false;
        }
    }
}
