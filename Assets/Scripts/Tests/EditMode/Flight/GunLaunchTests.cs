// GunLaunchTests.cs
// Secondary launch direction belongs to the hardpoint: WeaponMount.launchRotation -> WeaponMount.LaunchTurn -> Gun.launchTurn.
// Feature: Modding/README.md, ship mount "launchRotation".

using GoF2Remake.Data;
using GoF2Remake.Flight;
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

        static Vector3 Fire(Gun gun, Quaternion shipRotation)
        {
            Assert.AreEqual(0, gun.TryFire(Vector3.zero, shipRotation, false));
            return gun.bullets[0].velocity.normalized;
        }

        static void AssertDirection(Vector3 expected, Vector3 actual) =>
            Assert.That(Vector3.Distance(expected.normalized, actual), Is.LessThan(1e-4f), $"expected {expected}, got {actual}");

        [Test]
        public void AMountWithoutALaunchTurnsNothing()
        {
            Assert.AreEqual(Quaternion.identity, new WeaponMount().LaunchTurn);
            Assert.AreEqual(Quaternion.identity, new WeaponMount { launchRotation = new float[0] }.LaunchTurn);
        }

        [Test]
        public void TheMountsEulerRotationIsATurnInTheShipsFrame()
        {
            // Unity: pitch about +X with + = down, so -90 turns the nose up; yaw about +Y, + = right.
            var up = new WeaponMount { launchRotation = new[] { -90f, 0f, 0f } }.LaunchTurn;
            var right = new WeaponMount { launchRotation = new[] { 0f, 90f, 0f } }.LaunchTurn;
            AssertDirection(Vector3.up, up * Vector3.forward);
            AssertDirection(Vector3.right, right * Vector3.forward);
        }

        [Test]
        public void TheMountsQuaternionRotationIsRead()
        {
            var q = Quaternion.Euler(0f, 90f, 0f);
            var turn = new WeaponMount { launchRotation = new[] { q.x, q.y, q.z, q.w } }.LaunchTurn;
            AssertDirection(Vector3.right, turn * Vector3.forward);
        }

        [Test]
        public void TheVisualRotationAndUpsideDownDoNotTurnTheLaunch()
        {
            // a pod tilted for looks (rotation) or hung under the hull (upsideDown) keeps firing ahead: only launchRotation redirects
            var m = new WeaponMount { rotation = new[] { -90f, 0f, 0f }, upsideDown = true };
            Assert.AreEqual(Quaternion.identity, m.LaunchTurn);
        }

        [Test]
        public void TheSameMissileLeavesTwoMountsInTwoDirections()
        {
            var item = Missile();
            var top = new Gun(item, Vector3.zero, true) { launchTurn = new WeaponMount { launchRotation = new[] { -90f, 0f, 0f } }.LaunchTurn };
            var side = new Gun(item, Vector3.zero, true) { launchTurn = new WeaponMount { launchRotation = new[] { 0f, 90f, 0f } }.LaunchTurn };
            var plain = new Gun(item, Vector3.zero, true);
            AssertDirection(Vector3.up, Fire(top, Quaternion.identity));
            AssertDirection(Vector3.right, Fire(side, Quaternion.identity));
            AssertDirection(Vector3.forward, Fire(plain, Quaternion.identity));
            // the item's own stats are untouched by where it is mounted
            Assert.AreEqual(top.speedUnitsPerMs, side.speedUnitsPerMs, 1e-6f);
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
