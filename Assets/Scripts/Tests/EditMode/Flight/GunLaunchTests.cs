// GunLaunchTests.cs
// Remake mods (ItemStats 172-174): homingDelayMs (a homing shot flies straight for that long before it homes) and
// launchPitchDeg / launchYawDeg (the shot leaves the mount turned that many degrees up / to the right of the gun's line).
// Feature: Modding/README.md "Weapons" (homingDelayMs, launchPitchDeg, launchYawDeg).

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

        [Test]
        public void TheNamesMapToTheirAttributes()
        {
            Assert.AreEqual(172, ItemStats.IdOf("homingDelayMs"));
            Assert.AreEqual(173, ItemStats.IdOf("launchPitchDeg"));
            Assert.AreEqual(174, ItemStats.IdOf("launchYawDeg"));
        }

        [Test]
        public void WithoutTheAttributesThePlayersMissileHomesAtOnceAndGoesStraightAhead()
        {
            var gun = new Gun(Missile(), Vector3.zero, true);
            Assert.AreEqual(0f, gun.homingDelayMs, 1e-4f);
            Assert.AreEqual(Quaternion.identity, gun.launchTurn);
            Assert.That(Vector3.Distance(Vector3.forward, FireDirection(Missile(), Quaternion.identity)), Is.LessThan(1e-4f));
        }

        [Test]
        public void TheHomingDelayIsReadFromTheItem()
        {
            Assert.AreEqual(750f, new Gun(Missile(172, 750), Vector3.zero, true).homingDelayMs, 1e-4f);
        }

        [Test]
        public void ANegativeHomingDelayIsNone()
        {
            Assert.AreEqual(0f, new Gun(Missile(172, -5), Vector3.zero, true).homingDelayMs, 1e-4f);
        }

        [Test]
        public void ThePitchLaunchesTheShotUp()
        {
            var d = FireDirection(Missile(173, 90), Quaternion.identity);
            Assert.That(Vector3.Distance(Vector3.up, d), Is.LessThan(1e-4f));
        }

        [Test]
        public void ANegativePitchLaunchesTheShotDown()
        {
            var d = FireDirection(Missile(173, -90), Quaternion.identity);
            Assert.That(Vector3.Distance(Vector3.down, d), Is.LessThan(1e-4f));
        }

        [Test]
        public void TheYawLaunchesTheShotToTheRight()
        {
            var d = FireDirection(Missile(174, 90), Quaternion.identity);
            Assert.That(Vector3.Distance(Vector3.right, d), Is.LessThan(1e-4f));
        }

        [Test]
        public void TheLaunchTurnFollowsTheShipsOwnAxes()
        {
            // a ship turned 90 degrees to the right: its "up" is still up, its "right" is world back
            var ship = Quaternion.Euler(0f, 90f, 0f);
            Assert.That(Vector3.Distance(Vector3.up, FireDirection(Missile(173, 90), ship)), Is.LessThan(1e-4f));
            Assert.That(Vector3.Distance(Vector3.back, FireDirection(Missile(174, 90), ship)), Is.LessThan(1e-4f));
        }

        [Test]
        public void TheShotKeepsItsSpeed()
        {
            var gun = new Gun(Missile(173, 90, 174, 45), Vector3.zero, true);
            gun.TryFire(Vector3.zero, Quaternion.identity, false);
            Assert.AreEqual(20f * Gun.MetersPerUnit, gun.bullets[0].velocity.magnitude, 1e-4f);
        }

        [Test]
        public void TheAnglesAreClamped()
        {
            var gun = new Gun(Missile(173, 100000), Vector3.zero, true);
            gun.TryFire(Vector3.zero, Quaternion.identity, false);
            Assert.That(Vector3.Distance(Vector3.back, gun.bullets[0].velocity.normalized), Is.LessThan(1e-3f));   // 180 degrees up = backward
        }
    }
}
