// HangarFlight.cs
// Remake-only: a ship flying into or out of the docked station's hangar. The original cuts straight from space to the
// parked ship and back (MGame::dockEvent / ModStation::leaveStation); the lanes are StationTables.HangarLanes.
//   Arrival:   from 450 m outside the forcefield (growing from nothing to full size on the way, so it doesn't pop in
//              where the opening shows space), through it and across the room, level, on a centripetal Catmull-Rom
//              spline (full speed, then braking at a constant rate) toward the point over the pad (Vossk: in a wide arc
//              through the ring's centre and straight into the bay, at 'hover' height); a short rounded corner turns the
//              flight downward with a slight flare to a stop over the pad, the ship turns on the spot to its parking yaw
//              (a little bank into the turn) and sinks straight down onto the pad.
//   Departure: the same backwards: straight up without turning, a stop at the top to turn on the spot toward the way
//              out, the corner into level flight (nose down a little), then accelerating out through the forcefield,
//              shrinking away beyond it. While another ship flies ('Hold') it waits at the top after the turn.
//              Landings and take-offs are always vertical.
// The whole flight is one sampled path (lane + corner + vertical); the speed runs along it, stopping for the turn.
// Floor clearance: the bank and pitch are scaled down wherever they would dip the hull (its renderers' corners) below the
// height its bottom has parked plus FloorMargin (the Vossk bays are flown into 5 m up: the H'Soc's banked wing went ~3 m
// into the floor, the flare and the hover turn's bank a little).
// The engine exhaust (the NPC variant parts, the hangar's ships use them) and the engine loop run while it flies.
// Plain C#: StationLevel ticks it (the player) and HangarTraffic (the other ships).

using System.Collections.Generic;
using GoF2Remake.Data;
using GoF2Remake.Visuals;
using UnityEngine;

namespace GoF2Remake.World
{
    public sealed class HangarFlight
    {
        // Outside the forcefield the ships grow from nothing to full size (arriving) or shrink away (departing) over
        // 'OutsideDistance', so they don't pop in or out in view of the opening.
        const float OutsideDistance = 450f, InsideDistance = 60f;   // m (150 m: the growth / shrinking was over too fast)
        const float MaxSpeed = 200f, Accel = 60f;                     // the lane, m/s and m/s^2
        const float CornerSpeed = 16f, SettleAccel = 12f, SettleEndSpeed = 0.8f;   // the vertical part (m/s, m/s^2)
        const float GentleDistance = 25f, GentleAccel = 8f;   // the last metres to the stop over the pad (and away from it)
        const float CornerRadius = 8f;
        const float MaxBank = 0.6f, BankPerYawRate = 0.45f, MaxPitch = 0.6f, FlarePitch = 0.12f;
        const float TurnSpeed = 65f, MinTurnSeconds = 0.6f, HoverBank = 0.2f;   // the hover turn: deg/s, s, rad
        const int LaneSamples = 800, CornerSamples = 24, VerticalSamples = 8, FilletSamples = 12;
        const float FloorMargin = 0.4f;   // m above the parked bottom that a banked / pitched hull keeps
        // The ships also shrink away (grow in) where they leave (enter) the hangar camera's view: over the last (first)
        // ViewFade metres in view (at most ViewFadeShare of the lane in view, so a ship is full size before it slows down
        // over the pad), so they scale down instead of crossing the screen edge at full size.
        const float ViewFade = 160f, ViewFadeShare = 0.6f, ViewMargin = 0.05f;

        public readonly Transform ship;
        public readonly bool arriving;
        /// <summary>A departure stops at the top of its climb and waits while this is set (another ship is flying).</summary>
        public bool Hold;
        public bool Done { get; private set; }

        readonly List<Vector3> point = new List<Vector3>();
        readonly List<float> length = new List<float>();
        // Arc lengths along the path in its travel order: the lane ends / starts at sLane, the vertical part at sVertical.
        readonly float sLane, sVertical, total;
        readonly Vector3 pad, gate, outward, baseScale;
        readonly float padYaw, laneYaw;
        readonly AssembledObject asm;
        readonly Vector3[] hull;   // the renderers' bounds corners in the ship's unscaled local space
        readonly float floorY;     // the hull's lowest point when parked level on the pad (world y)
        readonly AudioSource engine;
        readonly float engineVolume;
        float s, v, holdT;
        /// <summary>Arc length where the path leaves the camera's view (departure) / enters it (arrival); -1 = never.</summary>
        float sViewEdge = -1f, viewFade = ViewFade;
        float scaleK = 1f;
        float yaw, pitch, bank, startYaw, cornerPitch, cornerBank;
        float turnT, turnSeconds, turnFrom, turnTo, holdBob;
        bool inLane, turning, turned;

        /// <summary>From space onto the pad at 'padPosition' (the ship's pivot), ending at 'finalRotation' (null = the
        /// heading it arrives with).</summary>
        public static HangarFlight Arrival(Transform ship, StationTables.HangarLane lane, Vector3 padPosition, Quaternion? finalRotation,
                                           AudioSource engine, float engineVolume) =>
            new HangarFlight(ship, true, lane, padPosition, finalRotation, engine, engineVolume);

        /// <summary>From the pad the ship is parked on out into space.</summary>
        public static HangarFlight Departure(Transform ship, StationTables.HangarLane lane, AudioSource engine, float engineVolume) =>
            new HangarFlight(ship, false, lane, ship.position, null, engine, engineVolume);

        HangarFlight(Transform ship, bool arriving, StationTables.HangarLane lane, Vector3 padPosition, Quaternion? finalRotation,
                     AudioSource engine, float engineVolume)
        {
            this.ship = ship;
            this.arriving = arriving;
            this.engine = engine;
            this.engineVolume = engineVolume;
            pad = padPosition;
            asm = ship.GetComponent<AssembledObject>();
            hull = HullCorners(ship);
            float restMin = 0f;
            for (int i = 0; i < hull.Length; i++) restMin = Mathf.Min(restMin, hull[i].y * ship.localScale.y);
            floorY = pad.y + restMin;

            // The lane's control points, from space to the point over the pad ('over'), level through the forcefield.
            var outward = lane.outward.normalized;
            var outside = lane.gate + outward * OutsideDistance;
            gate = lane.gate;
            this.outward = outward;
            baseScale = ship.localScale;
            var controls = new List<Vector3>();
            Vector3 over;
            if (lane.bays)
            {
                // Vossk: through the ring's centre (one wide arc, Fillet) and into the bay from its open side, which faces it.
                var inward = new Vector3(lane.hub.x - pad.x, 0f, lane.hub.z - pad.z).normalized;
                float bay = pad.y + lane.hover;
                float gateY = Mathf.Clamp(bay, lane.gateSpan.x, lane.gateSpan.y);
                over = new Vector3(pad.x, bay, pad.z);
                controls.Add(At(outside, gateY));
                controls.Add(At(lane.gate, gateY));
                controls.Add(At(lane.gate - outward * InsideDistance, gateY));
                controls.Add(new Vector3(lane.hub.x, bay, lane.hub.z));   // one wide arc through the centre, no hairpins
                controls.Add(over + inward * (lane.approach * 2.5f));
                controls.Add(over + inward * lane.approach);
            }
            else
            {
                float level = Mathf.Clamp(Mathf.Max(lane.cruise, pad.y + 15f), lane.gateSpan.x, lane.gateSpan.y);
                over = new Vector3(pad.x, Mathf.Max(level, pad.y + 15f), pad.z);
                controls.Add(At(outside, level));
                controls.Add(At(lane.gate, level));
                controls.Add(At(lane.gate - outward * InsideDistance, level));
            }

            // The corner: the lane ends 'r' short of 'over' (along its last horizontal direction), a quadratic curve with
            // 'over' as its control point turns it down to 'r' below it, then straight down onto the pad.
            var last = controls[controls.Count - 1];
            var d = new Vector3(over.x - last.x, 0f, over.z - last.z);
            d = d.sqrMagnitude > 1e-4f ? d.normalized : Vector3.forward;
            float r = Mathf.Min(CornerRadius, 0.45f * (over.y - pad.y));
            var cornerStart = over - d * r;
            var cornerEnd = over - Vector3.up * r;
            controls.Add(cornerStart);
            controls = Fillet(controls);

            var pts = new List<Vector3>();
            for (int i = 0; i <= LaneSamples; i++)
            {
                float t = (float)i / LaneSamples * (controls.Count - 1);
                int seg = Mathf.Min((int)t, controls.Count - 2);
                pts.Add(CatmullRom(controls, seg, t - seg));
            }
            int laneEnd = pts.Count - 1;
            for (int i = 1; i <= CornerSamples; i++)
            {
                float t = (float)i / CornerSamples;
                pts.Add((1 - t) * (1 - t) * cornerStart + 2 * (1 - t) * t * over + t * t * cornerEnd);
            }
            int verticalStart = pts.Count - 1;
            for (int i = 1; i <= VerticalSamples; i++) pts.Add(Vector3.Lerp(cornerEnd, pad, (float)i / VerticalSamples));
            if (!arriving)
            {
                pts.Reverse();
                int n = pts.Count - 1;
                (laneEnd, verticalStart) = (n - laneEnd, n - verticalStart);
            }
            for (int i = 0; i < pts.Count; i++)
            {
                point.Add(pts[i]);
                length.Add(i == 0 ? 0f : length[i - 1] + Vector3.Distance(pts[i - 1], pts[i]));
            }
            total = length[length.Count - 1];
            sLane = length[laneEnd];
            sVertical = length[verticalStart];

            sViewEdge = ViewEdge(Camera.main);
            if (sViewEdge >= 0f)
            {
                float inView = arriving ? sLane - sViewEdge : sViewEdge - sLane;   // the lane's part in view
                viewFade = Mathf.Clamp(inView * ViewFadeShare, 1f, ViewFade);
            }
            laneYaw = Heading(arriving ? d : -d);
            padYaw = finalRotation.HasValue ? finalRotation.Value.eulerAngles.y : laneYaw;
            startYaw = ship.eulerAngles.y;

            // The NPC engine parts, or the engine glow for a ship without them (the Kaamo Club's 55-63 flew without flames).
            if (asm != null) asm.SetExhaust(true, !asm.HasNpcExhaust);
            if (engine != null && engine.clip != null)
            {
                engine.volume = arriving ? engineVolume : engineVolume * 0.25f;
                engine.Play();
            }
            if (arriving)
            {
                v = MaxSpeed;
                Pose(0f);
            }
        }

        static Vector3 At(Vector3 p, float y) => new Vector3(p.x, y, p.z);

        /// <summary>Where the sampled path crosses the edge of 'cam''s view: a departure's first point out of it (after
        /// the climb), an arrival's first point of the stretch that stays in it; -1 when it never leaves / is never out.</summary>
        float ViewEdge(Camera cam)
        {
            if (cam == null) return -1f;
            bool InView(Vector3 p)
            {
                var q = cam.WorldToViewportPoint(p);
                return q.z > 0f && q.x > -ViewMargin && q.x < 1f + ViewMargin && q.y > -ViewMargin && q.y < 1f + ViewMargin;
            }
            if (arriving)
            {
                for (int i = point.Count - 1; i >= 0; i--)
                    if (!InView(point[i])) return i + 1 < point.Count ? length[i + 1] : -1f;
                return -1f;
            }
            for (int i = 0; i < point.Count; i++)
                if (length[i] > sVertical && !InView(point[i])) return length[i];
            return -1f;
        }

        /// <summary>The 8 corners of every mesh renderer's local bounds, in the ship's own (unscaled) space.</summary>
        static Vector3[] HullCorners(Transform ship)
        {
            var list = new List<Vector3>();
            var toShip = ship.worldToLocalMatrix;
            foreach (var r in ship.GetComponentsInChildren<MeshRenderer>())
            {
                var lb = r.localBounds;
                var m = toShip * r.transform.localToWorldMatrix;
                for (int i = 0; i < 8; i++)
                    list.Add(m.MultiplyPoint3x4(lb.center + Vector3.Scale(lb.extents,
                        new Vector3((i & 1) == 0 ? -1 : 1, (i & 2) == 0 ? -1 : 1, (i & 4) == 0 ? -1 : 1))));
            }
            return list.ToArray();
        }

        /// <summary>The rotation for 'pos' with the pitch and bank scaled down (never the yaw) just enough that the hull
        /// stays FloorMargin above the parked floor level; level (the vertical parts) it can't dip, so that is left.</summary>
        Quaternion Clear(Vector3 pos, float pitchRad, float yawDeg, float bankRad)
        {
            Quaternion Rot(float k) => Quaternion.Euler(pitchRad * k * Mathf.Rad2Deg, yawDeg, bankRad * k * Mathf.Rad2Deg);
            var q = Rot(1f);
            if (hull.Length == 0 || (pitchRad == 0f && bankRad == 0f) || Lowest(pos, q) >= floorY + FloorMargin) return q;
            if (Lowest(pos, Rot(0f)) < floorY + FloorMargin) return Rot(0f);
            float lo = 0f, hi = 1f;
            for (int i = 0; i < 10; i++)
            {
                float mid = (lo + hi) * 0.5f;
                if (Lowest(pos, Rot(mid)) >= floorY + FloorMargin) lo = mid; else hi = mid;
            }
            return Rot(lo);
        }

        float Lowest(Vector3 pos, Quaternion q)
        {
            var sc = ship.localScale;
            float min = float.MaxValue;
            for (int i = 0; i < hull.Length; i++) min = Mathf.Min(min, (q * Vector3.Scale(hull[i], sc)).y);
            return pos.y + min;
        }

        /// <summary>Rounds every bend of the control polyline with a wide quadratic curve (the bend's point as its control
        /// point) that starts and ends up to 90 % of the way along the neighbouring straights (45 % where the neighbour
        /// bends too), so the spline sweeps round instead of pivoting at the point (the Vossk ring's centre).</summary>
        static List<Vector3> Fillet(List<Vector3> p)
        {
            int n = p.Count;
            if (n < 3) return p;
            bool Bends(int i) => i > 0 && i < n - 1 && Vector3.Angle(p[i] - p[i - 1], p[i + 1] - p[i]) > 2f;
            var result = new List<Vector3> { p[0] };
            for (int i = 1; i < n - 1; i++)
            {
                var d1 = p[i] - p[i - 1];
                var d2 = p[i + 1] - p[i];
                float l1 = d1.magnitude, l2 = d2.magnitude;
                if (!Bends(i) || l1 < 1f || l2 < 1f) { result.Add(p[i]); continue; }
                float k1 = Bends(i - 1) ? 0.45f : 0.9f, k2 = Bends(i + 1) ? 0.45f : 0.9f;
                float t = Mathf.Min(k1 * l1, k2 * l2);
                var a = p[i] - d1 / l1 * t;
                var b = p[i] + d2 / l2 * t;
                for (int j = 0; j <= FilletSamples; j++)
                {
                    float u = (float)j / FilletSamples;
                    result.Add((1 - u) * (1 - u) * a + 2 * (1 - u) * u * p[i] + u * u * b);
                }
            }
            result.Add(p[n - 1]);
            return result;
        }

        /// <summary>Centripetal Catmull-Rom (alpha 0.5) through p[seg] .. p[seg + 1]; the ends are mirrored.</summary>
        static Vector3 CatmullRom(List<Vector3> p, int seg, float u)
        {
            var p1 = p[seg];
            var p2 = p[seg + 1];
            var p0 = seg > 0 ? p[seg - 1] : p1 + (p1 - p2);
            var p3 = seg + 2 < p.Count ? p[seg + 2] : p2 + (p2 - p1);
            float t0 = 0f;
            float t1 = t0 + Mathf.Sqrt(Mathf.Max(Vector3.Distance(p0, p1), 1e-3f));
            float t2 = t1 + Mathf.Sqrt(Mathf.Max(Vector3.Distance(p1, p2), 1e-3f));
            float t3 = t2 + Mathf.Sqrt(Mathf.Max(Vector3.Distance(p2, p3), 1e-3f));
            float t = Mathf.Lerp(t1, t2, u);
            var a1 = (t1 - t) / (t1 - t0) * p0 + (t - t0) / (t1 - t0) * p1;
            var a2 = (t2 - t) / (t2 - t1) * p1 + (t - t1) / (t2 - t1) * p2;
            var a3 = (t3 - t) / (t3 - t2) * p2 + (t - t2) / (t3 - t2) * p3;
            var b1 = (t2 - t) / (t2 - t0) * a1 + (t - t0) / (t2 - t0) * a2;
            var b2 = (t3 - t) / (t3 - t1) * a2 + (t - t1) / (t3 - t1) * a3;
            return (t2 - t) / (t2 - t1) * b1 + (t - t1) / (t2 - t1) * b2;
        }

        Vector3 PointAt(float dist)
        {
            if (dist <= 0f) return point[0];
            if (dist >= total) return point[point.Count - 1];
            int lo = 0, hi = point.Count - 1;
            while (hi - lo > 1)
            {
                int mid = (lo + hi) / 2;
                if (length[mid] < dist) lo = mid; else hi = mid;
            }
            float k = (dist - length[lo]) / Mathf.Max(length[hi] - length[lo], 1e-4f);
            return Vector3.Lerp(point[lo], point[hi], k);
        }

        /// <summary>The lane's direction at 'dist' (sampled inside the lane only, so the corner doesn't tip the nose).</summary>
        Vector3 Tangent(float dist)
        {
            float lo = arriving ? 0f : sLane, hi = arriving ? sLane : total;
            var d = PointAt(Mathf.Min(dist + 2f, hi)) - PointAt(Mathf.Max(dist - 2f, lo));
            return d.sqrMagnitude > 1e-6f ? d.normalized : ship.forward;
        }

        static float Heading(Vector3 dir) => Mathf.Atan2(dir.x, dir.z) * Mathf.Rad2Deg;

        // ---- per frame -----------------------------------------------------------------------------------

        public void Update(float dt)
        {
            if (Done || dt <= 0f) return;
            if (turning) { Turn(dt); return; }
            if (arriving)
            {
                if (!turned)   // full speed, braking hard, then gently over the last GentleDistance to a stop over the pad
                    v = Glide(sVertical - s);
                else   // straight down
                    v = Vertical(s - sVertical, total - s);
            }
            else if (!turned)   // straight up, stopping at the top
                v = Vertical(s, sVertical - s);
            else if (Hold)   // waiting over the pad for the lane
            {
                holdT += dt;
                holdBob = Mathf.Sin(holdT * 1.3f) * 0.5f;
                Pose(dt);
                return;
            }
            else   // gently away from the stop through the corner, then hard out
            {
                v = Glide(s - sVertical);
                holdBob = Mathf.MoveTowards(holdBob, 0f, dt);   // the waiting bob settles
            }

            s = Mathf.Min(total, s + v * dt);
            if (!turned && s >= sVertical - 0.02f && (arriving ? s > sLane : true))
            {
                // At the top of the vertical part: turn on the spot (to the parking yaw / toward the way out).
                s = sVertical;
                v = 0f;
                turning = true;
                turnT = 0f;
                turnFrom = arriving ? laneYaw : startYaw;
                turnTo = arriving ? padYaw : laneYaw;
                turnSeconds = Mathf.Max(MinTurnSeconds, Mathf.Abs(Mathf.DeltaAngle(turnFrom, turnTo)) / TurnSpeed);
            }
            Pose(dt);
            if (s >= total) Finish();
        }

        /// <summary>The speed 'd' metres from the stop over the pad (either way): constant braking at GentleAccel over the last
        /// GentleDistance, at Accel before that, up to MaxSpeed.</summary>
        static float Glide(float d)
        {
            d = Mathf.Max(0f, d);
            float v2 = SettleEndSpeed * SettleEndSpeed + 2f * GentleAccel * Mathf.Min(d, GentleDistance) + 2f * Accel * Mathf.Max(0f, d - GentleDistance);
            return Mathf.Min(MaxSpeed, Mathf.Sqrt(v2));
        }

        /// <summary>The vertical part: speeding up from one end and braking into the other at SettleAccel.</summary>
        static float Vertical(float fromStart, float toEnd)
        {
            float a = Mathf.Sqrt(SettleEndSpeed * SettleEndSpeed + 2f * SettleAccel * Mathf.Max(0f, fromStart));
            float b = Mathf.Sqrt(SettleEndSpeed * SettleEndSpeed + 2f * SettleAccel * Mathf.Max(0f, toEnd));
            return Mathf.Min(CornerSpeed, Mathf.Min(a, b));
        }

        /// <summary>The hover turn: an eased yaw with a slight bank into it.</summary>
        void Turn(float dt)
        {
            turnT += dt;
            float u = Mathf.Clamp01(turnT / turnSeconds);
            float before = yaw;
            yaw = Mathf.LerpAngle(turnFrom, turnTo, Mathf.SmoothStep(0f, 1f, u));
            float yawRate = Mathf.DeltaAngle(before, yaw) * Mathf.Deg2Rad / dt;
            bank = Mathf.Clamp(-yawRate * BankPerYawRate, -HoverBank, HoverBank);
            pitch = 0f;
            var pos = PointAt(s) + Vector3.up * (Mathf.Sin(Mathf.PI * u) * 0.3f);   // a slight lift, back at the end
            ApplyScale(pos);
            ship.SetPositionAndRotation(pos, Clear(pos, 0f, yaw, bank));
            if (u < 1f) return;
            turning = false;
            turned = true;
            bank = 0f;
            v = 0f;
        }

        void Pose(float dt)
        {
            var pos = PointAt(s);
            bool lane = arriving ? s < sLane : s > sLane;
            float vol = engineVolume;   // the lane: full; the corner and the vertical parts below set less
            if (lane)
            {
                var dir = Tangent(s);
                float heading = Heading(dir);
                if (!inLane) { yaw = heading; inLane = true; }
                float yawRate = dt > 0f ? Mathf.DeltaAngle(yaw, heading) * Mathf.Deg2Rad / dt : 0f;
                yaw = heading;
                bank = Mathf.Lerp(bank, Mathf.Clamp(-yawRate * BankPerYawRate, -MaxBank, MaxBank), 1f - Mathf.Exp(-dt * 4f));
                pitch = Mathf.Clamp(-Mathf.Asin(Mathf.Clamp(dir.y, -1f, 1f)), -MaxPitch, MaxPitch);
                cornerPitch = pitch;
                cornerBank = bank;
            }
            else if (arriving && !turned)
            {
                // The corner: heading kept, a flare (nose up a little), level wings.
                inLane = false;
                float corner = Mathf.Clamp01((s - sLane) / Mathf.Max(sVertical - sLane, 1e-3f));
                yaw = laneYaw;
                pitch = Mathf.Lerp(cornerPitch, 0f, corner) - FlarePitch * Mathf.Sin(Mathf.PI * corner);
                bank = Mathf.Lerp(cornerBank, 0f, corner);
                vol = engineVolume * Mathf.Lerp(1f, 0.5f, corner);
            }
            else if (arriving)
            {
                // Straight down at the parking yaw.
                yaw = padYaw;
                pitch = bank = 0f;
                float down = Mathf.Clamp01((s - sVertical) / Mathf.Max(total - sVertical, 1e-3f));
                vol = engineVolume * Mathf.Lerp(0.5f, 0.25f, down);
            }
            else if (!turned)
            {
                // Straight up at the parked yaw.
                yaw = startYaw;
                pitch = bank = 0f;
                vol = engineVolume * Mathf.Lerp(0.25f, 0.5f, Mathf.Clamp01(s / Mathf.Max(sVertical, 1e-3f)));
            }
            else
            {
                // The corner into level flight: nose down a little as it speeds up.
                float corner = Mathf.Clamp01((s - sVertical) / Mathf.Max(sLane - sVertical, 1e-3f));
                yaw = laneYaw;
                pitch = FlarePitch * Mathf.Sin(Mathf.PI * corner);
                bank = 0f;
                vol = engineVolume * Mathf.Lerp(0.5f, 1f, corner);
            }
            ApplyScale(pos);
            if (engine != null) engine.volume = vol * scaleK;   // fading out (in) with the ship's size
            pos += Vector3.up * holdBob;
            ship.SetPositionAndRotation(pos, Clear(pos, pitch, yaw, bank));
        }

        /// <summary>Full size at the forcefield, nothing at the far end outside; and nothing where the path leaves (enters)
        /// the camera's view, from full size viewFade metres before (after) it.</summary>
        void ApplyScale(Vector3 pos)
        {
            float beyond = Vector3.Dot(pos - gate, outward);
            float k = 1f - Mathf.SmoothStep(0f, 1f, beyond / OutsideDistance);
            if (sViewEdge >= 0f)
                k = Mathf.Min(k, arriving ? Mathf.SmoothStep(0f, 1f, (s - sViewEdge) / viewFade)
                                          : 1f - Mathf.SmoothStep(0f, 1f, (s - (sViewEdge - viewFade)) / viewFade));
            scaleK = Mathf.Max(0.001f, k);
            ship.localScale = baseScale * scaleK;
        }

        /// <summary>Arrival: straight onto the pad. Departure: gone.</summary>
        public void Skip()
        {
            if (Done) return;
            if (arriving) ship.SetPositionAndRotation(pad, Quaternion.Euler(0f, padYaw, 0f));
            Finish();
        }

        void Finish()
        {
            Done = true;
            // A departure stays gone: back at full size it showed for the frame before the next scene loaded.
            if (arriving) ship.localScale = baseScale;
            else ship.gameObject.SetActive(false);
            if (arriving) ship.SetPositionAndRotation(pad, Quaternion.Euler(0f, padYaw, 0f));
            if (engine != null) engine.Stop();
            if (arriving && asm != null) asm.SetExhaust(false, !asm.HasNpcExhaust);   // parked: exhaust off (createShip / setExhaustVisible)
        }

        /// <summary>The engine loop for a ship in the hangar: the player's own (PlayerEngine's pick) or a random NPC engine
        /// (sound 46), 3D, at their event volumes with the space engines' rolloff (EngineVoices.Setup3D).</summary>
        public static AudioSource AddEngine(GameObject ship, bool player, Database db, int shipIndex, out float volume)
        {
            volume = 0f;
            AudioClip clip;
            if (player) clip = GoF2Remake.Flight.PlayerEngine.EngineClip(db, shipIndex, out volume);
            else
            {
                var assets = GoF2Remake.Flight.CombatAssets.Load();
                clip = assets != null ? GoF2Remake.Flight.CombatAssets.Pick(assets.enemyEngines) : null;
                volume = 0.0759f * GoF2Remake.Flight.Sfx.EventGain;   // event 46
            }
            if (clip == null) return null;
            var src = ship.AddComponent<AudioSource>();
            src.playOnAwake = false;
            src.loop = true;
            src.clip = clip;
            // As the engines in space (EngineVoices: the events' linear rolloff to 500 m from the listener), not 40-1500 m:
            // a ship leaving the room fades with the distance instead of staying loud until it is cut off.
            GoF2Remake.Flight.EngineVoices.Setup3D(src);
            volume *= Settings.SfxVolume;
            return src;
        }
    }
}
