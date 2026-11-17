// GunLaunchTests.cs
// Player secondary launch direction comes from its hardpoint, not the weapon's visual rotation.

using GoF2Remake.Data;
using GoF2Remake.Flight;
using NUnit.Framework;
using UnityEngine;

namespace GoF2Remake.Tests
{
    public class GunLaunchTests
    {
        static ItemData Missile()
        {
            return new ItemData
            {
                index = 100,
                categoryId = (int)Gun.Kind.Missile,
                attrKeys = new[] { 13, 12 },
                attrValues = new[] { 20, 1000 }
            };
        }

        static Vector3 Fire(Gun gun, Quaternion shipRotation)
        {
            Assert.AreEqual(0, gun.TryFire(Vector3.zero, shipRotation, false));
            return gun.bullets[0].velocity.normalized;
        }

        static void AssertDirection(Vector3 expected, Vector3 actual) =>
            Assert.That(Vector3.Distance(expected.normalized, actual), Is.LessThan(1e-4f),
                $"expected {expected}, got {actual}");

        [Test]
        public void MissingLaunchRotationMeansStraightAhead()
        {
            Assert.AreEqual(Quaternion.identity, new WeaponMount().LaunchTurn);
            Assert.AreEqual(Quaternion.identity,
                new WeaponMount { launchRotation = new float[0] }.LaunchTurn);
        }

        [Test]
        public void EulerLaunchRotationIsRelativeToTheShip()
        {
            var up = new WeaponMount { launchRotation = new[] { -90f, 0f, 0f } }.LaunchTurn;
            var right = new WeaponMount { launchRotation = new[] { 0f, 90f, 0f } }.LaunchTurn;
            AssertDirection(Vector3.up, up * Vector3.forward);
            AssertDirection(Vector3.right, right * Vector3.forward);
        }

        [Test]
        public void QuaternionLaunchRotationIsSupported()
        {
            var q = Quaternion.Euler(0f, 90f, 0f);
            var turn = new WeaponMount { launchRotation = new[] { q.x, q.y, q.z, q.w } }.LaunchTurn;
            AssertDirection(Vector3.right, turn * Vector3.forward);
        }

        [Test]
        public void CosmeticMountRotationDoesNotChangeLaunchDirection()
        {
            var mount = new WeaponMount
            {
                rotation = new[] { -90f, 0f, 0f },
                upsideDown = true
            };
            Assert.AreEqual(Quaternion.identity, mount.LaunchTurn);
        }

        [Test]
        public void SameMissileCanLeaveDifferentHardpointsInDifferentDirections()
        {
            var item = Missile();
            var upper = new Gun(item, Vector3.zero, true)
            {
                launchTurn = new WeaponMount { launchRotation = new[] { -90f, 0f, 0f } }.LaunchTurn
            };
            var side = new Gun(item, Vector3.zero, true)
            {
                launchTurn = new WeaponMount { launchRotation = new[] { 0f, 90f, 0f } }.LaunchTurn
            };
            var defaultGun = new Gun(item, Vector3.zero, true);

            AssertDirection(Vector3.up, Fire(upper, Quaternion.identity));
            AssertDirection(Vector3.right, Fire(side, Quaternion.identity));
            AssertDirection(Vector3.forward, Fire(defaultGun, Quaternion.identity));
            Assert.AreEqual(upper.speedUnitsPerMs, side.speedUnitsPerMs, 1e-6f);
        }

        [Test]
        public void LaunchRotationUsesTheShipsWorldOrientation()
        {
            var ship = Quaternion.Euler(0f, 90f, 0f);
            var upper = new Gun(Missile(), Vector3.zero, true)
            {
                launchTurn = Quaternion.Euler(-90f, 0f, 0f)
            };
            var side = new Gun(Missile(), Vector3.zero, true)
            {
                launchTurn = Quaternion.Euler(0f, 90f, 0f)
            };
            AssertDirection(Vector3.up, Fire(upper, ship));
            AssertDirection(Vector3.back, Fire(side, ship));
        }

        [Test]
        public void LockDelayIsASeparatePerMountMillisecondSetting()
        {
            var mount = new WeaponMount { lockDelayMs = 1000 };
            Assert.AreEqual(1000, mount.lockDelayMs);
            Assert.AreEqual(0, new WeaponMount().lockDelayMs);
        }

        [Test]
        public void LaunchRotationDoesNotChangeProjectileSpeed()
        {
            var gun = new Gun(Missile(), Vector3.zero, true)
            {
                launchTurn = Quaternion.Euler(-45f, 30f, 0f)
            };
            gun.TryFire(Vector3.zero, Quaternion.identity, false);
            Assert.AreEqual(20f * Gun.MetersPerUnit, gun.bullets[0].velocity.magnitude, 1e-4f);
        }
    }
}
