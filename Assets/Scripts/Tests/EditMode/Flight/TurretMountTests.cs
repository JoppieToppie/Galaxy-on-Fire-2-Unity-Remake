// TurretMountTests.cs
// Feature: a turret mount facing any way from the hull (Data.WeaponMount.turretUp, PlayerTurret.PlaceOnMount): the turn
// that puts the turret's up onto the facing, the old top / under-hull mounts unchanged, which facings hang the turret view's
// camera, and the Mod Builder's snapping of a hull's surface normal to the ship's axes.

using GoF2Remake.Data;
using GoF2Remake.Flight;
using NUnit.Framework;
using UnityEngine;

namespace GoF2Remake.Tests
{
    public class TurretMountTests
    {
        static WeaponMount Facing(float x, float y, float z) => new WeaponMount { slotType = 2, turretUp = new[] { x, y, z } };

        static void Near(Vector3 expected, Vector3 actual, string what) =>
            Assert.Less(Vector3.Distance(expected, actual), 1e-4f, $"{what}: {actual} (expected {expected})");

        [Test]
        public void APlainMountStaysUpright()
        {
            var m = new WeaponMount { slotType = 2 };
            Assert.AreEqual(Quaternion.identity, m.TurretTurn);
            Assert.IsFalse(m.TurretHangs);
        }

        [Test]
        public void UpsideDownIsAHalfTurnAboutTheLength()
        {
            var m = new WeaponMount { slotType = 2, upsideDown = true };
            Near(Vector3.down, m.TurretTurn * Vector3.up, "up");
            Near(Vector3.forward, m.TurretTurn * Vector3.forward, "its rest heading stays the nose");
            Assert.IsTrue(m.TurretHangs);
            Near(Vector3.down, Facing(0f, -1f, 0f).TurretTurn * Vector3.up, "turretUp [0, -1, 0] is the same");
            Assert.AreEqual(m.TurretTurn, Facing(0f, -1f, 0f).TurretTurn);
        }

        [Test]
        public void ASideTurretFacesOutOfThatSide()
        {
            // Game +x is the pilot's left, Unity's -x (the models are mirrored).
            var left = Facing(1f, 0f, 0f);
            Near(Vector3.left, left.TurretTurn * Vector3.up, "left side");
            Near(Vector3.forward, left.TurretTurn * Vector3.forward, "still aimed at the nose at rest");
            Assert.IsFalse(left.TurretHangs, "a side turret's view keeps its own up");
            Near(Vector3.right, Facing(-1f, 0f, 0f).TurretTurn * Vector3.up, "right side");
        }

        [Test]
        public void AnyAngleAndAnyLength()
        {
            var m = Facing(2f, 2f, 0f);
            Near(new Vector3(-1f, 1f, 0f).normalized, m.TurretTurn * Vector3.up, "45 deg up on the left");
            Assert.IsTrue(Facing(1f, -1f, 0f).TurretHangs, "below the horizon hangs");
            Near(Vector3.forward, Facing(0f, 0f, 3f).TurretTurn * Vector3.up, "on the nose");
        }

        [Test]
        public void TurretUpWinsOverUpsideDown()
        {
            var m = Facing(0f, 1f, 0f);
            m.upsideDown = true;
            Near(Vector3.up, m.TurretTurn * Vector3.up, "turretUp");
            Assert.AreEqual(Vector3.up, new WeaponMount { turretUp = new[] { 0f, 0f, 0f } }.TurretUpLocal, "a zero vector is the plain mount");
        }

        [Test]
        public void TurretMountScaleDefaultsToOneAndAllowsLargeValues()
        {
            Assert.AreEqual(1f, new WeaponMount { slotType = 2 }.TurretScale);
            Assert.AreEqual(1f, new WeaponMount { slotType = 2, turretScale = 0f }.TurretScale, "an omitted/deserialized zero uses the legacy default");
            Assert.AreEqual(2.5f, new WeaponMount { slotType = 2, turretScale = 2.5f }.TurretScale);
            Assert.AreEqual(1000f, new WeaponMount { slotType = 2, turretScale = 2500f }.TurretScale, "runtime scale is capped at 1000x");
            Assert.AreEqual(1f, new WeaponMount { slotType = 2, turretScale = float.NaN }.TurretScale, "invalid values fall back to normal size");
        }

        [Test]
        public void PlaceOnMountMultipliesTheTurretPrefabScale()
        {
            var go = new GameObject("turret scale test");
            try
            {
                go.transform.localScale = new Vector3(2f, 3f, 4f);
                var mount = new WeaponMount { slotType = 2, turretScale = 2.5f, position_engine = new[] { 0, 0, 0 } };
                TurretCore.PlaceOnMount(go.transform, mount, Quaternion.identity);
                Assert.AreEqual(new Vector3(5f, 7.5f, 10f), go.transform.localScale);
            }
            finally { Object.DestroyImmediate(go); }
        }

        [Test]
        public void TheBuilderSnapsNearlyAxisNormals()
        {
            var m = new WeaponMount { slotType = 2, upsideDown = true };
            m.SetTurretUp(new Vector3(0.1f, 0.99f, 0.05f), true);
            CollectionAssert.AreEqual(new[] { 0f, 1f, 0f }, m.turretUp);
            Assert.IsFalse(m.upsideDown, "turretUp says it now");
            m.SetTurretUp(new Vector3(0.7f, 0.7f, 0f), true);
            Assert.AreEqual(0.707f, m.turretUp[0], 1e-6f, "a slope stays as it is");
            m.SetTurretUp(new Vector3(0.1f, 0.99f, 0f));
            Assert.AreNotEqual(0f, m.turretUp[0], "without snap the exact direction");
        }
    }
}
