// KhadorTimelineTests.cs
// Feature: the hole the Khador Slipstream tears into interstellar space (Flight.KhadorTimeline, World.KhadorFx,
// docs/notes/star-map.md "Interstellar space"), and the sky that crosses a leg with the ship (InterstellarSpace.SkyShare).

using GoF2Remake.Data;
using GoF2Remake.Flight;
using NUnit.Framework;
using UnityEngine;

namespace GoF2Remake.Tests
{
    public class KhadorTimelineTests
    {
        [Test]
        public void TheElementsComeInTheOriginalsOrder()
        {
            Assert.That(KhadorTimeline.Beam(200f), Is.GreaterThan(0.9f), "the beam strikes first");
            Assert.That(KhadorTimeline.Web(200f), Is.Zero, "the web comes after it");
            Assert.That(KhadorTimeline.Web(1000f), Is.GreaterThan(0.9f));
            Assert.That(KhadorTimeline.Ring(900f), Is.Zero, "the ring after the web");
            Assert.That(KhadorTimeline.Ring(1400f), Is.GreaterThan(0.9f));
            Assert.That(KhadorTimeline.Flash(1500f, 1500f), Is.EqualTo(1f));
            Assert.That(KhadorTimeline.Beam(KhadorTimeline.LengthMs), Is.Zero);
        }

        [Test]
        public void TheShipEasesBackThenIsDrawnIn()
        {
            Assert.That(KhadorTimeline.RiftSpeed(0f), Is.EqualTo(2f), "the scene's speed, no jump from the ship's own");
            Assert.That(KhadorTimeline.RiftSpeed(KhadorTimeline.RiftPullMs - 1f), Is.LessThan(1.51f));
            Assert.That(KhadorTimeline.RiftSpeed(KhadorTimeline.RiftPullMs + 700f), Is.GreaterThan(2f));
            float cross = KhadorTimeline.RiftCrossMs(KhadorTimeline.RiftFocusUnits);
            Assert.That(cross, Is.GreaterThan(KhadorTimeline.RiftPullMs), "it reaches the hole only after it has been drawn in");
        }

        [Test]
        public void TheWarpStartsAsTheShipIsDrawnInAndIsFullAtTheHole()
        {
            float cross = KhadorTimeline.RiftCrossMs(KhadorTimeline.RiftFocusUnits);
            Assert.That(KhadorTimeline.RiftWarp(KhadorTimeline.RiftPullMs, cross), Is.Zero);
            Assert.That(KhadorTimeline.RiftWarp((KhadorTimeline.RiftPullMs + cross) / 2f, cross), Is.EqualTo(0.5f).Within(0.01f));
            Assert.That(KhadorTimeline.RiftWarp(cross, cross), Is.EqualTo(1f));
        }

        [Test]
        public void TheHoleGrowsAndFramesTheShipAsItCrosses()
        {
            const float cross = 4000f, ship = 700f;
            float rate = KhadorTimeline.RiftRate, at = cross * rate;
            Assert.That(KhadorTimeline.RiftUnits(KhadorTimeline.RingStartMs - 1f, at, ship), Is.Zero);
            Assert.That(KhadorTimeline.RiftUnits(at, at, ship), Is.GreaterThan(ship), "wider than the ship as it goes through");
            float last = 0f;
            for (float ms = KhadorTimeline.RingStartMs; ms < at + 300f; ms += 20f)
            {
                float units = KhadorTimeline.RiftUnits(ms, at, ship);
                Assert.That(units, Is.GreaterThanOrEqualTo(last));
                last = units;
            }
        }

        [Test]
        public void ACircleFillsAViewOnlyPastItsCorners()
        {
            const float tan = 0.7f, aspect = 16f / 9f;
            float corner = tan * Mathf.Sqrt(1f + aspect * aspect);
            Assert.That(KhadorTimeline.Fills(corner * 100f * 0.99f, 100f, tan, aspect), Is.False);
            Assert.That(KhadorTimeline.Fills(corner * 100f * 1.01f, 100f, tan, aspect), Is.True);
            Assert.That(KhadorTimeline.Fills(1f, -5f, tan, aspect), Is.True, "past the plane all that is seen is the far side");
        }

        [Test]
        public void TheSkyShowsTheDistanceFlown()
        {
            Assert.That(InterstellarSpace.SkyShare(InterstellarSpace.StartUnits, 600000f), Is.Zero, "the start is the sky left");
            Assert.That(InterstellarSpace.SkyShare(1000000f, InterstellarSpace.ArriveUnits), Is.EqualTo(1f), "an arrival finds its sky");
            float half = InterstellarSpace.SkyShare(InterstellarSpace.StartUnits + 300000f, InterstellarSpace.ArriveUnits + 300000f);
            Assert.That(half, Is.EqualTo(0.5f).Within(0.001f), "half way across a leg, half way between the skies");
            float last = 0f;
            for (float flown = 0f; flown <= 600000f; flown += 30000f)
            {
                float share = InterstellarSpace.SkyShare(InterstellarSpace.StartUnits + flown, InterstellarSpace.ArriveUnits + 600000f - flown);
                Assert.That(share, Is.GreaterThanOrEqualTo(last));
                last = share;
            }
        }
    }
}
