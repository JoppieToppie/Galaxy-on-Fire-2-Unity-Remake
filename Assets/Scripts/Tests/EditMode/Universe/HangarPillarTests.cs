// HangarPillarTests.cs
// Feature: CLAUDE.md "Station scene" > Hangar flights (remake): the Nivelian hangar's lanes steer round its roof pillars
// (StationTables.HangarLane.pillars, HangarFlight.AvoidPillars); a player's report 2026-10-16: ships flew through the pillar
// in front of the right pad. Expected values: the pillars measured on hangar_nivelian's mesh (StationTables), the pads
// (the turntable at the origin, ParkedSlots[2] through OrbitLayout.ToUnity); along every arrival and departure the ship's
// outline from above (its width x length, facing along the path) keeps clear of each pillar's radius. Also where the
// ships shrink at the edge of the view: the hangar camera's own view, not Inspect ship's close-up.

using System.Linq;
using GoF2Remake.World;
using NUnit.Framework;
using UnityEngine;

namespace GoF2Remake.Tests
{
    public class HangarPillarTests
    {
        GameObject ship;
        Vector3 size;   // the ship's own scale (an arrival grows it from nothing outside the forcefield)

        [TearDown]
        public void TearDown() { if (ship != null) Object.DestroyImmediate(ship); }

        /// <summary>The Nivelian hangar's pads: the turntable and every parked slot (8 m up); [1] the right pad, (204.8, 0).</summary>
        static readonly Vector3[] NivelianPads =
            new[] { new Vector3(0f, 8f, 0f) }.Concat(StationTables.ParkedSlots[2].Select(s => OrbitLayout.ToUnity(s) + Vector3.up * 8f)).ToArray();

        /// <summary>The least gap (metres) between the ship's outline from above (a 'width' x 'length' rectangle round the
        /// pivot, facing along the path) and any pillar's surface over the flight's level part (the vertical part is over
        /// the pad).</summary>
        static float LeastClearance(HangarFlight f, Vector3[] pillars, float width, float length)
        {
            float least = float.MaxValue;
            var path = f.Path;
            for (int k = 1; k < path.Count; k++)
            {
                var fwd = new Vector2(path[k].x - path[k - 1].x, path[k].z - path[k - 1].z);
                if (fwd.sqrMagnitude < 1e-4f) continue;
                fwd.Normalize();
                var right = new Vector2(fwd.y, -fwd.x);
                foreach (var q in pillars)
                {
                    var rel = new Vector2(q.x - path[k].x, q.y - path[k].z);
                    float dx = Mathf.Max(0f, Mathf.Abs(Vector2.Dot(rel, right)) - width / 2f);
                    float dz = Mathf.Max(0f, Mathf.Abs(Vector2.Dot(rel, fwd)) - length / 2f);
                    least = Mathf.Min(least, new Vector2(dx, dz).magnitude - q.z);
                }
            }
            return least;
        }

        HangarFlight Fly(StationTables.HangarLane lane, Vector3 pad, bool arriving, HangarFlight.View? view = null)
        {
            ship.transform.position = pad;
            ship.transform.rotation = Quaternion.identity;
            ship.transform.localScale = size;
            return arriving ? HangarFlight.Arrival(ship.transform, lane, pad, null, null, 0f, view)
                            : HangarFlight.Departure(ship.transform, lane, null, 0f, view);
        }

        [TestCase(20f, 30f)]   // a fighter
        [TestCase(45f, 75f)]   // a big hull
        [TestCase(90f, 125f)]  // the biggest that dock (the Rhino, a Most Wanted ship)
        public void NivelianFlightsClearThePillars(float width, float length)
        {
            var lane = StationTables.HangarLanes[2];
            Assert.IsNotNull(lane.pillars);
            Assert.AreEqual(12, lane.pillars.Length);   // the remodelled room's (2026-11-01; the original's 6)
            Assert.AreEqual(6, NivelianPads.Length);
            ship = GameObject.CreatePrimitive(PrimitiveType.Cube);
            size = new Vector3(width, 8f, length);
            foreach (var pad in NivelianPads)
                foreach (bool arriving in new[] { true, false })
                {
                    float clear = LeastClearance(Fly(lane, pad, arriving), lane.pillars, width, length);
                    Assert.GreaterOrEqual(clear, 1f, $"pad {pad}, {(arriving ? "arriving" : "departing")}: {clear:F1} m");
                }
        }

        /// <summary>The Vossk hangar's pads: the turntable and every parked slot (OrbitLayout.ToUnity, 10 m up).</summary>
        static Vector3[] VosskPads() =>
            new[] { new Vector3(0f, 10f, 0f) }.Concat(StationTables.ParkedSlots[1].Select(s => OrbitLayout.ToUnity(s) + Vector3.up * 10f)).ToArray();

        [TestCase(20f, 30f)]
        [TestCase(45f, 75f)]
        [TestCase(90f, 125f)]
        public void VosskFlightsGoRoundTheColumn(float width, float length)
        {
            // the remodelled Vossk hangar (2026-10-24): a column of radius 100 m at the hub, where the lane used to cross
            var lane = StationTables.HangarLanes[1];
            Assert.Greater(lane.column, 0f);
            var column = new[] { new Vector3(lane.hub.x, lane.hub.z, lane.column) };
            ship = GameObject.CreatePrimitive(PrimitiveType.Cube);
            size = new Vector3(width, 8f, length);
            foreach (var pad in VosskPads())
                foreach (bool arriving in new[] { true, false })
                {
                    float clear = LeastClearance(Fly(lane, pad, arriving), column, width, length);
                    Assert.GreaterOrEqual(clear, 1f, $"pad {pad}, {(arriving ? "arriving" : "departing")}: {clear:F1} m");
                }
        }

        [Test]
        public void WithoutTheColumnTheFarPadsLaneCrossesIt()
        {
            var lane = StationTables.HangarLanes[1];
            var bare = new StationTables.HangarLane { gate = lane.gate, outward = lane.outward, gateSpan = lane.gateSpan, bays = true,
                                                      hub = lane.hub, approach = lane.approach, hover = lane.hover };
            ship = GameObject.CreatePrimitive(PrimitiveType.Cube);
            size = new Vector3(20f, 8f, 30f);
            var far = OrbitLayout.ToUnity(new Vector3(10691f, 0f, 12745f)) + Vector3.up * 10f;   // across the room from the portal
            Assert.Less(LeastClearance(Fly(bare, far, true), new[] { new Vector3(lane.hub.x, lane.hub.z, 100f) }, 20f, 30f), 0f);
        }

        [Test]
        public void WithoutTheAvoidanceTheRightPadsLaneHitsAPillar()
        {
            // the report's case: the straight run to the right pad passed ~7 m from the pillar at (303, -307)
            var lane = StationTables.HangarLanes[2];
            var bare = new StationTables.HangarLane { gate = lane.gate, outward = lane.outward, gateSpan = lane.gateSpan, cruise = lane.cruise };
            ship = GameObject.CreatePrimitive(PrimitiveType.Cube);
            size = new Vector3(20f, 8f, 30f);
            Assert.Less(LeastClearance(Fly(bare, NivelianPads[1], true), lane.pillars, 20f, 30f), 0f);
        }

        // 2026-11-17 (a player's report): NPC ships taking off in the Vossk hangar shrank to nothing just over their pads
        // during Inspect ship. HangarFlight measured where the path leaves the view against the camera as it was, the
        // orbit's close-up; StationLevel passes the hangar camera's own view (HangarFlight.View) instead.
        [Test]
        public void TheShrinkFollowsTheViewItIsGiven()
        {
            var lane = StationTables.HangarLanes[1];
            var pad = VosskPads()[3];
            ship = GameObject.CreatePrimitive(PrimitiveType.Cube);
            size = new Vector3(20f, 8f, 30f);
            // the top of the climb, looked at from 80 m further from the hub: a close-up like Inspect ship's
            var top = Fly(lane, pad, false).Path.First(q => new Vector2(q.x - pad.x, q.z - pad.z).magnitude > 1f);
            var away = new Vector3(pad.x - lane.hub.x, 0f, pad.z - lane.hub.z).normalized;
            var closeAt = top + away * 80f;
            var closeUp = new HangarFlight.View { position = closeAt, rotation = Quaternion.LookRotation(top - closeAt), verticalFovDeg = 50f, aspect = 16f / 9f };
            // high over the hub looking down, the whole room and the space outside the portal in view
            var whole = new HangarFlight.View { position = lane.hub + Vector3.up * 3000f, rotation = Quaternion.LookRotation(Vector3.down, Vector3.forward),
                                                verticalFovDeg = 120f, aspect = 1f };
            Assert.Less(ScaleAway(Fly(lane, pad, false, closeUp), pad, 250f), 0.01f, "out of the close-up's view: shrunk away");
            Assert.AreEqual(1f, ScaleAway(Fly(lane, pad, false, whole), pad, 250f), 1e-3f, "in the room's view: full size");
        }

        /// <summary>Flies 'f' until the ship is 'distance' metres (across the floor) from 'pad': its scale then, of its own.</summary>
        float ScaleAway(HangarFlight f, Vector3 pad, float distance)
        {
            for (int i = 0; i < 20000 && !f.Done; i++)
            {
                f.Update(0.02f);
                var p = ship.transform.position;
                if (new Vector2(p.x - pad.x, p.z - pad.z).magnitude >= distance) return ship.transform.localScale.x / size.x;
            }
            Assert.Fail("the ship never got " + distance + " m from its pad");
            return 0f;
        }

        [Test]
        public void TheViewProjectsLikeACamera()
        {
            var go = new GameObject("view test camera");
            try
            {
                var cam = go.AddComponent<Camera>();
                cam.fieldOfView = 47f;
                cam.aspect = 16f / 9f;
                go.transform.SetPositionAndRotation(new Vector3(30f, 80f, -400f), Quaternion.Euler(12f, 25f, 3f));
                var view = HangarFlight.View.Of(cam).Value;
                foreach (var p in new[] { new Vector3(0f, 0f, 0f), new Vector3(200f, -50f, 300f), new Vector3(-150f, 120f, 50f) })
                {
                    Vector3 a = cam.WorldToViewportPoint(p), b = view.Viewport(p);
                    Assert.AreEqual(a.x, b.x, 1e-3f, p.ToString());
                    Assert.AreEqual(a.y, b.y, 1e-3f, p.ToString());
                    Assert.AreEqual(a.z, b.z, 1e-2f, p.ToString());
                }
            }
            finally { Object.DestroyImmediate(go); }
        }
    }
}
