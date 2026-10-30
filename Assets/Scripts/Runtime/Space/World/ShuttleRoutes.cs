// ShuttleRoutes.cs
// NPC shuttles on a docking route (Route(..., 9), PlayerFighter states 7-9, case 9): station -> waypoint -> drop-off and
// back, docking on an object's pads (SpacePoints) for the route's stay. Used by the Supernova levels 94 / 102 / 154
// (SupernovaLevels). Plain C# driven by its owner's frame.

using System.Collections.Generic;
using GoF2Remake.Data;
using UnityEngine;

namespace GoF2Remake.World
{
    public class ShuttleRoutes
    {
        const float M = 0.05f;

        static Vector3 ToUnity(Vector3 game) => new Vector3(game.x, game.y, -game.z) * M;

        public class Shuttle
        {
            public NpcShip ship;
            public Vector3[] points;   // game units
            public float[] dockMs;
            public int dropOff, leg;
            public float perUnitMs, waitMs, tickMs;
            public bool rhino, waiting;
            // The object each leg docks at (null = a plain waypoint) and its SpacePoints set (PlayerFighter states 7-9).
            public Transform[] dockAt;
            public int[] dockSets;
            public int dockPhase;            // 0 flying to the approach point, 1 easing onto the pad, 2 docked
            public bool once, finished;      // once: the route ends at its last point (154's freighter), no loop
            public int taken = -1;           // the docking point index taken on dockAt[leg]
            public Vector3 approachLocal, pivotLocal, fromPos;
            public Quaternion localRot, fromRot;
            public float easeMs;
        }
        public readonly List<Shuttle> All = new List<Shuttle>();
        public void Add(Shuttle sh) => All.Add(sh);

        /// <summary>PlayerFighter states 7 / 8 (game/PlayerFighter.c 2779-2890): the nearest free approach point (type 1), reached
        /// within 2000 units; then the docking point nearest it, eased onto like the player's (approachDockingPoint: nose = the
        /// point's direction, up = from the docking point to the approach point, stopping the ship's height short of it,
        /// DAT_00254820 = StationTables.ShipY) over 3000 ms (the iOS build's PlayerFighter::update: EaseInOutMatrix(.., 3000) on
        /// entering state 8; the player's own approachDockingPoint eases over 2000); state 9
        /// docked, the exhaust off. Returns true once docked.</summary>
        bool Docking(Shuttle sh, Transform obj, float dtMs)
        {
            var s = sh.ship;
            var points = SpacePoints.Set(sh.dockSets[sh.leg]);
            if (sh.taken < 0)
            {
                // Pick the nearest free approach point and the docking point nearest it.
                float best = float.MaxValue;
                int pick = -1;
                for (int i = 0; i < points.Count; i++)
                {
                    if (points[i].type != SpacePoints.Approach || SpacePoints.IsTaken(obj, i)) continue;
                    float d = (obj.TransformPoint(SpacePoints.ToLocal(points[i].engine)) - s.transform.position).sqrMagnitude;
                    if (d < best) { best = d; pick = i; }
                }
                if (pick < 0) return false;   // every pad busy: hold here (the waypoint logic below keeps it flying)
                var a = SpacePoints.ToLocal(points[pick].engine);
                best = float.MaxValue;
                int dock = -1;
                for (int i = 0; i < points.Count; i++)
                {
                    if (points[i].type != SpacePoints.Dock) continue;
                    float d = (SpacePoints.ToLocal(points[i].engine) - a).sqrMagnitude;
                    if (d < best) { best = d; dock = i; }
                }
                if (dock < 0) return false;
                sh.taken = pick;
                SpacePoints.Take(obj, pick);
                var dLocal = SpacePoints.ToLocal(points[dock].engine);
                var up = a - dLocal;
                up = up.sqrMagnitude > 1e-8f ? up.normalized : Vector3.up;
                var nose = SpacePoints.DirToLocal(points[dock].dir);
                sh.approachLocal = a;
                sh.pivotLocal = dLocal + up * (StationTables.ShipY(s.Spec.ship) * M);
                sh.localRot = Quaternion.LookRotation(nose.sqrMagnitude > 1e-6f ? nose : Vector3.forward, up);
                sh.dockPhase = 0;
            }
            if (sh.dockPhase == 0)
            {
                var to = obj.TransformPoint(sh.approachLocal) - s.transform.position;
                if (to.magnitude / M > 2000f)
                {
                    s.scriptedSpeed = 2f;
                    s.transform.rotation = Quaternion.RotateTowards(s.transform.rotation, Quaternion.LookRotation(to.normalized, Vector3.up), 0.05f * dtMs);
                    return false;
                }
                sh.dockPhase = 1;
                sh.easeMs = 0f;
                sh.fromPos = s.transform.position;
                sh.fromRot = s.transform.rotation;
            }
            if (sh.dockPhase == 1)
            {
                s.scriptedSpeed = 0f;
                sh.easeMs += dtMs;
                float t = Mathf.SmoothStep(0f, 1f, Mathf.Clamp01(sh.easeMs / 3000f));
                s.transform.SetPositionAndRotation(Vector3.Lerp(sh.fromPos, obj.TransformPoint(sh.pivotLocal), t),
                                                   Quaternion.Slerp(sh.fromRot, obj.rotation * sh.localRot, t));
                if (t < 1f) return false;
                sh.dockPhase = 2;
                s.SetExhaust(false);
            }
            // Docked: stay on the pad (the object may move).
            s.scriptedSpeed = 0f;
            s.transform.SetPositionAndRotation(obj.TransformPoint(sh.pivotLocal), obj.rotation * sh.localRot);
            return true;
        }

        /// <summary>Every pad of the leg's object taken: hover 5000 units further out than the nearest approach point.</summary>
        void HoldOffPads(Shuttle sh, Transform obj, float dtMs)
        {
            var s = sh.ship;
            var points = SpacePoints.Set(sh.dockSets[sh.leg]);
            Vector3 hold = obj.position;
            float best = float.MaxValue;
            for (int i = 0; i < points.Count; i++)
            {
                if (points[i].type != SpacePoints.Approach) continue;
                var a = obj.TransformPoint(SpacePoints.ToLocal(points[i].engine));
                float d = (a - s.transform.position).sqrMagnitude;
                if (d >= best) continue;
                best = d;
                var outward = a - obj.position;
                hold = a + (outward.sqrMagnitude > 1e-6f ? outward.normalized : Vector3.up) * (5000f * M);
            }
            var to = hold - s.transform.position;
            if (to.magnitude / M < 1500f) { s.scriptedSpeed = 0f; return; }
            s.scriptedSpeed = 2f;
            s.transform.rotation = Quaternion.RotateTowards(s.transform.rotation, Quaternion.LookRotation(to.normalized, Vector3.up), 0.05f * dtMs);
        }

        /// <summary>On to the next leg; a route that runs once (154's freighter) holds at its last point.</summary>
        public static void NextLeg(Shuttle sh)
        {
            if (sh.once && sh.leg == sh.points.Length - 1) { sh.finished = true; sh.ship.scriptedSpeed = 0f; return; }
            sh.leg = (sh.leg + 1) % sh.points.Length;
        }

        /// <summary>Leaving a pad: the point is free again, the exhaust on; the ship flies off nose first.</summary>
        public void Undock(Shuttle sh)
        {
            var obj = sh.dockAt != null && sh.leg < sh.dockAt.Length ? sh.dockAt[sh.leg] : null;
            if (obj != null && sh.taken >= 0) SpacePoints.Free(obj, sh.taken);
            if (sh.taken >= 0) sh.ship.SetExhaust(true);
            sh.taken = -1;
            sh.dockPhase = 0;
        }

        /// <summary>The loops with their docking times (a stop at an object on its pads, else at the waypoint); 'waitingAt' runs
        /// every frame a shuttle waits at a stop.</summary>
        public void Update(float dtMs, System.Action<Shuttle, float> waitingAt = null)
        {
            foreach (var sh in All)
            {
                var s = sh.ship;
                if (s == null || !s.Target.Alive || s.Gone || sh.finished) continue;
                var target = sh.points[sh.leg];
                if (sh.waiting)
                {
                    s.scriptedSpeed = 0f;
                    sh.waitMs += dtMs;
                    waitingAt?.Invoke(sh, dtMs);   // a stop's own work (94 / 102: the evacuees go aboard)
                    if (sh.waitMs < sh.dockMs[sh.leg]) continue;
                    sh.waiting = false;
                    Undock(sh);
                    NextLeg(sh);
                    continue;
                }
                // A leg at an object: its pads (the original's docking route waypoint), not the object's centre.
                var dockObj = sh.dockAt != null && sh.leg < sh.dockAt.Length ? sh.dockAt[sh.leg] : null;
                if (dockObj != null)
                {
                    if (Docking(sh, dockObj, dtMs))
                    {
                        sh.waiting = sh.dockMs[sh.leg] > 0f;
                        sh.waitMs = sh.tickMs = 0f;
                        if (!sh.waiting) { Undock(sh); NextLeg(sh); }
                    }
                    if (sh.taken >= 0) continue;
                    // Remake: no free pad (another ship or the player on it): wait 5000 units out from the nearest one (it
                    // fell through to the object's centre and "docked" inside the hull, at 102 inside Tadram).
                    HoldOffPads(sh, dockObj, dtMs);
                    continue;
                }
                var to = ToUnity(target) - s.transform.position;
                if (to.magnitude / M < 2500f)
                {
                    sh.waiting = sh.dockMs[sh.leg] > 0f;
                    sh.waitMs = sh.tickMs = 0f;
                    if (!sh.waiting) NextLeg(sh);
                    continue;
                }
                s.scriptedSpeed = 2f;
                var want = Quaternion.LookRotation(to.normalized, Vector3.up);
                s.transform.rotation = Quaternion.RotateTowards(s.transform.rotation, want, 0.05f * dtMs);
            }
        }
    }
}
