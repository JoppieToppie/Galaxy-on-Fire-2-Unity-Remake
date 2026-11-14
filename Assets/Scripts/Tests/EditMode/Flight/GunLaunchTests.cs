// GunLaunchTests.cs
// Remake mods: homingDelayMs (ItemStats 172, an item property: a homing shot flies straight that long before it homes) and
// a secondary's launch direction, which belongs to its hardpoint (WeaponMount.rotation -> WeaponMount.RotationTurn -> Gun.launchTurn,
// set by WeaponSystem.Setup): the same missile on two mounts leaves in two directions.
// Feature: Modding/README.md "Weapons" (homingDelayMs) and the mount's "rotation".

using GoF2Remake.Data;
using GoF2Remake.Flight;
using GoF2Remake.Modding;
using NUnit.Framework;
using UnityEngine;

namespace GoF2Remake.Tests
{
    public class GunLaunchTests
    {
        /// <summary>A missile (sort 5) with the given attribute pairs (attr, value, attr, value...), speed 20 u/ms, 1 s lifetime.</summary>
        static ItemData Missile(params int[] pairs)
        {
            var keys = new int[pairs.Length / 2 + 2];
            var values = new int[pairs.Length / 2 + 2];
            keys[0] = 13; values[0] = 20;
            keys[1] = 12; values[1] = 1000;
            for (int i = 0; i < pairs.Length / 2; i++) { keys[i + 2] = pairs[2 * i]; values[i + 2] = pairs[2 * i + 1]; }
            return new ItemData { index = 100, categoryId = (int)Gun.Kind.Missile, attrKeys = keys, attrValues = values };
        }

        static Vector3 FireDirection(ItemData item, Quaternion shipRotation)
        {
            var gun = new Gun(item, Vector3.zero, true);
            Assert.AreEqual(0, gun.TryFire(Vector3.zero, shipRotation, false));
            return gun.bullets[0].velocity.normalized;
        }

        static Vector3 Fire(Gun gun, Quaternion shipRotation)
        {
            Assert.AreEqual(0, gun.TryFire(Vector3.zero, shipRotation, false));
            return gun.bullets[0].velocity.normalized;
        }

        static void AssertDirection(Vector3 expected, Vector3 actual) =>
            Assert.That(Vector3.Distance(expected.normalized, actual), Is.LessThan(1e-4f), $"expected {expected}, got {actual}");

        [Test]
        public void TheHomingDelayNameMapsToItsAttribute() => Assert.AreEqual(172, ItemStats.IdOf("homingDelayMs"));

        [Test]
        public void TheItemNoLongerCarriesALaunchAngle()
        {
            Assert.AreEqual(-1, ItemStats.IdOf("launchPitchDeg"));
            Assert.AreEqual(-1, ItemStats.IdOf("launchYawDeg"));
        }

        [Test]
        public void WithoutAnythingThePlayersMissileHomesAtOnceAndGoesStraightAhead()
        {
            var gun = new Gun(Missile(), Vector3.zero, true);
            Assert.AreEqual(0f, gun.homingDelayMs, 1e-4f);
            Assert.AreEqual(Quaternion.identity, gun.launchTurn);
            AssertDirection(Vector3.forward, Fire(gun, Quaternion.identity));
        }

        [Test]
        public void TheHomingDelayIsReadFromTheItem() =>
            Assert.AreEqual(750f, new Gun(Missile(172, 750), Vector3.zero, true).homingDelayMs, 1e-4f);

        [Test]
        public void ANegativeHomingDelayIsNone() =>
            Assert.AreEqual(0f, new Gun(Missile(172, -5), Vector3.zero, true).homingDelayMs, 1e-4f);

        [Test]
        public void AMountWithoutARotationTurnsNothing()
        {
            Assert.AreEqual(Quaternion.identity, new WeaponMount().RotationTurn);
            Assert.AreEqual(Quaternion.identity, new WeaponMount { rotation = new float[0] }.RotationTurn);
        }

        [Test]
        public void TheMountsEulerRotationIsATurnInTheShipsFrame()
        {
            // Unity: pitch about +X with + = down, so -90 turns the nose up; yaw about +Y, + = right.
            var up = new WeaponMount { rotation = new[] { -90f, 0f, 0f } }.RotationTurn;
            var right = new WeaponMount { rotation = new[] { 0f, 90f, 0f } }.RotationTurn;
            AssertDirection(Vector3.up, up * Vector3.forward);
            AssertDirection(Vector3.right, right * Vector3.forward);
        }

        [Test]
        public void TheMountsQuaternionRotationIsRead()
        {
            var q = Quaternion.Euler(0f, 90f, 0f);
            var turn = new WeaponMount { rotation = new[] { q.x, q.y, q.z, q.w } }.RotationTurn;
            AssertDirection(Vector3.right, turn * Vector3.forward);
        }

        [Test]
        public void UpsideDownIsNotPartOfTheLaunchTurn()
        {
            // a secondary hung under the hull still fires ahead: the half turn about the ship's length is for its model only
            Assert.AreEqual(Quaternion.identity, new WeaponMount { upsideDown = true }.RotationTurn);
        }

        [Test]
        public void TheSameMissileLeavesTwoMountsInTwoDirections()
        {
            var item = Missile(172, 400);
            var top = new Gun(item, Vector3.zero, true) { launchTurn = new WeaponMount { rotation = new[] { -90f, 0f, 0f } }.RotationTurn };
            var side = new Gun(item, Vector3.zero, true) { launchTurn = new WeaponMount { rotation = new[] { 0f, 90f, 0f } }.RotationTurn };
            var plain = new Gun(item, Vector3.zero, true);
            AssertDirection(Vector3.up, Fire(top, Quaternion.identity));
            AssertDirection(Vector3.right, Fire(side, Quaternion.identity));
            AssertDirection(Vector3.forward, Fire(plain, Quaternion.identity));
            // the item's own stats are untouched by where it is mounted
            Assert.AreEqual(top.speedUnitsPerMs, side.speedUnitsPerMs, 1e-6f);
            Assert.AreEqual(top.homingDelayMs, side.homingDelayMs, 1e-6f);
            Assert.AreEqual(top.damage, plain.damage);
        }

        [Test]
        public void TheLaunchTurnFollowsTheShipsOwnAxes()
        {
            // a ship turned 90 degrees to the right: its "up" is still up, its "right" is world back
            var ship = Quaternion.Euler(0f, 90f, 0f);
            var topMount = new Gun(Missile(), Vector3.zero, true) { launchTurn = Quaternion.Euler(-90f, 0f, 0f) };
            var sideMount = new Gun(Missile(), Vector3.zero, true) { launchTurn = Quaternion.Euler(0f, 90f, 0f) };
            AssertDirection(Vector3.up, Fire(topMount, ship));
            AssertDirection(Vector3.back, Fire(sideMount, ship));
        }

        [Test]
        public void TheShotKeepsItsSpeedWhateverTheMountsTurn()
        {
            var gun = new Gun(Missile(), Vector3.zero, true) { launchTurn = Quaternion.Euler(-45f, 30f, 0f) };
            gun.TryFire(Vector3.zero, Quaternion.identity, false);
            Assert.AreEqual(20f * Gun.MetersPerUnit, gun.bullets[0].velocity.magnitude, 1e-4f);
        }
    }
}
