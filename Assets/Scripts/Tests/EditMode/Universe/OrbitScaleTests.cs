// OrbitScaleTests.cs
// Feature: docs/notes/space-scene.md "Station scale" (OrbitScale): the experimental option that grows the orbit's
// structures and maps every station-anchored position radially (x F inside the station's reach R, eased between R and
// 2R, + 1.5 (F - 1) R beyond), the identity at 1x, in multiplayer and without a station.

using GoF2Remake.Data;
using GoF2Remake.World;
using NUnit.Framework;
using UnityEngine;

namespace GoF2Remake.Tests
{
    [Unity.Scripting.LifecycleManagement.NoAutoStaticsCleanup]
    public class OrbitScaleTests : SandboxTest
    {
        [TearDown]
        public void ForgetOrbit() => OrbitScale.Use(null);

        [Test]
        public void ConstructionDoesNotTreatOccupiedOrWreckedOrbitsAsVacant()
        {
            Assert.IsFalse(OrbitLayout.VacantStationSite(Session.VoidOrbit, 20));
            Assert.IsFalse(OrbitLayout.VacantStationSite(41, 20));
            Assert.IsFalse(OrbitLayout.VacantStationSite(110, 20));
            Assert.IsTrue(OrbitLayout.VacantStationSite(102, 20));
        }

        [Test]
        public void TheMapScalesInsideTheReachAndShiftsBeyond()
        {
            const float f = 4f, r = 40000f;
            Assert.AreEqual(10000f * f, OrbitScale.MapRadius(10000f, f, r), 1e-2f);
            Assert.AreEqual(r * f, OrbitScale.MapRadius(r, f, r), 1e-2f);
            Assert.AreEqual(300000f + 1.5f * (f - 1f) * r, OrbitScale.MapRadius(300000f, f, r), 1e-1f);
            Assert.AreEqual(2f * r + 1.5f * (f - 1f) * r, OrbitScale.MapRadius(2f * r, f, r), 1e-1f);   // continuous at 2R
        }

        [Test]
        public void TheMapIsContinuousMonotonicAndInvertible()
        {
            foreach (float f in new[] { 1.5f, 2f, 4f, 6f })
            {
                const float reach = 25000f;
                float last = -1f;
                for (float x = 0f; x < 6f * reach; x += 37f)
                {
                    float y = OrbitScale.MapRadius(x, f, reach);
                    Assert.Greater(y, last, $"monotonic at {x} (x{f})");
                    Assert.Less(y - last, f * 37f + 1f, $"no jump at {x} (x{f})");
                    Assert.AreEqual(x, OrbitScale.UnmapRadius(y, f, reach), 0.05f, $"inverse at {x} (x{f})");
                    last = y;
                }
                var p = new Vector3(31000f, -12000f, 52000f);
                var q = OrbitScale.MapPoint(p, f, reach);
                Assert.AreEqual(0f, Vector3.Angle(p, q), 1e-3f, "radial: the direction stays");
                Assert.Less((OrbitScale.UnmapPoint(q, f, reach) - p).magnitude, 0.1f);
            }
        }

        [Test]
        public void FarGroupsKeepTheirSpacing()
        {
            // beyond 2R the orbit is pushed out: spacing along the line from the station stays, across it grows by
            // r' / r only (1.54 here, 250000 units out at x4 with a 30000 reach), not by the station's 4
            var a = new Vector3(0f, 0f, 250000f);
            float along = (OrbitScale.MapPoint(a, 4f, 30000f) - OrbitScale.MapPoint(a + new Vector3(0f, 0f, 2000f), 4f, 30000f)).magnitude;
            Assert.AreEqual(2000f, along, 1f);
            float across = (OrbitScale.MapPoint(a, 4f, 30000f) - OrbitScale.MapPoint(a + new Vector3(2000f, 0f, 0f), 4f, 30000f)).magnitude;
            Assert.AreEqual(2000f * (250000f + 1.5f * 3f * 30000f) / 250000f, across, 5f);
        }

        [Test]
        public void NothingOutsideTheStationEndsUpInsideTheGrownStation()
        {
            // anything at or beyond the reach lands at or beyond reach x F, where the grown station ends
            const float f = 4f, reach = 30000f;
            for (float x = reach; x < 5f * reach; x += 500f) Assert.GreaterOrEqual(OrbitScale.MapRadius(x, f, reach), reach * f - 0.5f);
        }

        [Test]
        public void TheOptionIsOffByDefaultAndTheMapIsTheIdentity()
        {
            Assert.AreEqual(1f, Settings.StationScale);
            var layout = OrbitLayout.Build(Db, 0);
            OrbitScale.Use(layout);
            Assert.IsFalse(OrbitScale.Active);
            Assert.AreEqual(1f, OrbitScale.Factor);
            var p = new Vector3(1234f, 5678f, 91011f);
            Assert.AreEqual(p, OrbitScale.Place(p));
            Assert.AreEqual(OrbitLayout.ToUnity(p), OrbitScale.ToUnity(p));
            Assert.AreEqual(OrbitLayout.GateRadius, layout.JumpgateRadius);
        }

        [Test]
        public void AScaledOrbitPlacesItsGateArrivalAndRadiusWithTheStation()
        {
            using (new SettingsScope<float>(() => Settings.StationScale, v => Settings.StationScale = v))
            {
                Settings.StationScale = 4f;
                Assert.AreEqual(4f, Settings.StationScale);
                var plain = OrbitLayout.Build(Db, 0);   // station 0 has the system's gate
                Assert.IsTrue(plain.Scaled);
                Assert.Greater(plain.stationReach, 10000f);
                Assert.AreEqual(4f, plain.StationScale);
                Assert.AreEqual(OrbitLayout.GateRadius * 4, plain.JumpgateRadius);
                OrbitScale.Use(plain);
                // the arrival lies on the same line from the station, pushed out by the station's growth
                plain.ArrivalPose(-1, out var arrival, out _);
                var unscaled = OrbitLayout.ToUnity(plain.hiddenJumpgate);
                Assert.AreEqual(0f, Vector3.Angle(arrival, unscaled), 0.01f);
                Assert.AreEqual(plain.PlaceRadius(plain.hiddenJumpgate.magnitude) * OrbitLayout.MetersPerUnit, arrival.magnitude, 0.5f);
                // a Unity position read back gives the original's coordinates
                var g = new Vector3(5000f, 200f, 9000f);
                Assert.Less((OrbitScale.ToGame(OrbitScale.ToUnity(g)) - g).magnitude, 0.5f);
            }
        }

        [Test]
        public void AJumpgateMinimumCanScaleAnOrbitWithoutAStation()
        {
            var layout = new OrbitLayout { hasJumpgate = true, minimumJumpgateScale = 2.5f };
            OrbitScale.Use(layout);
            Assert.AreEqual(1f, layout.StationScale);
            Assert.AreEqual(2.5f, OrbitScale.JumpgateFactor);
            Assert.AreEqual(Mathf.RoundToInt(OrbitLayout.GateRadius * 2.5f), layout.JumpgateRadius);
        }

        [Test]
        public void TheDefaultArrivalClearsAnEnlargedGate()
        {
            var layout = new OrbitLayout
            {
                hasJumpgate = true, minimumJumpgateScale = 6f,
                jumpgate = new Vector3(0f, 0f, 90000f), hiddenJumpgate = new Vector3(0f, 0f, 120000f)
            };
            layout.ArrivalPose(-1, out var arrival, out _);
            float distance = Vector3.Distance(arrival, OrbitLayout.ToUnity(layout.jumpgate));
            Assert.Greater(distance, layout.JumpgateRadius * OrbitLayout.MetersPerUnit);
        }

        [Test]
        public void StationScaleSnapsToTheOfferedChoices()
        {
            using (new SettingsScope<float>(() => Settings.StationScale, v => Settings.StationScale = v))
            {
                Settings.StationScale = 3.7f;
                Assert.AreEqual(4f, Settings.StationScale);
                Settings.StationScale = 40f;
                Assert.AreEqual(OrbitScale.Max, Settings.StationScale);
                Settings.StationScale = 0.2f;
                Assert.AreEqual(1f, Settings.StationScale);
            }
        }

        [Test]
        public void AnOrbitWithoutAStationIsNotScaled()
        {
            using (new SettingsScope<float>(() => Settings.StationScale, v => Settings.StationScale = v))
            {
                Settings.StationScale = 4f;
                var empty = OrbitLayout.Build(Db, 102);   // an empty orbit (Status::inEmptyOrbit)
                Assert.IsFalse(empty.Scaled);
                Assert.AreEqual(1f, empty.StationScale);
                var p = new Vector3(0f, 0f, 50000f);
                Assert.AreEqual(p, empty.Place(p));
            }
        }
    }
}
