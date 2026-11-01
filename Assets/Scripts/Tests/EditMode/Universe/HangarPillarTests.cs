// HangarPillarTests.cs
// Feature: CLAUDE.md "Station scene" > Hangar flights (remake): the Nivelian hangar's lanes steer round its roof pillars
// (StationTables.HangarLane.pillars, HangarFlight.AvoidPillars); a player's report 2026-10-16: ships flew through the pillar
// in front of the right pad. Expected values: the pillars measured on hangar_nivelian's mesh (StationTables), the pads
// (the turntable at the origin, ParkedSlots[2] through OrbitLayout.ToUnity); along every arrival and departure the ship's
// outline from above (its width x length, facing along the path) keeps clear of each pillar's radius.

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

        HangarFlight Fly(StationTables.HangarLane lane, Vector3 pad, bool arriving)
        {
            ship.transform.position = pad;
            ship.transform.rotation = Quaternion.identity;
            ship.transform.localScale = size;
            return arriving ? HangarFlight.Arrival(ship.transform, lane, pad, null, null, 0f) : HangarFlight.Departure(ship.transform, lane, null, 0f);
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
    }
}
