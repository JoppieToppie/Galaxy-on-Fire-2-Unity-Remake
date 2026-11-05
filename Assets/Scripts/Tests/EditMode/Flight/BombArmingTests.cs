// BombArmingTests.cs
// Feature: weapons.md "Special weapons" / Options > Gameplay "Bomb arming distance" (Settings.BombArming, Gun.BombArmed;
// players' report 2026-11-04: nukes, EMP bombs and Ion Lambdas went off at launch with enemies about). Expected behaviour:
// under the original's rule a bomb ignites in any ship's hit cube from the launch (a fighter's +-1000 units, so 35-65 m
// in front of the player); with the option on its contact fuse arms after Gun.BombArmingUnits of flight, while asteroids
// still die on contact and a second press still sets it off at once.

using System.Collections.Generic;
using GoF2Remake.Data;
using GoF2Remake.Flight;
using NUnit.Framework;
using UnityEngine;

namespace GoF2Remake.Tests
{
    public class BombArmingTests : SandboxTest
    {
        const float M = Gun.MetersPerUnit;
        const int Extinctor = 46;
        readonly List<GameObject> made = new List<GameObject>();
        System.IDisposable collision, arming;
        Vector3? blast;

        void OnDetonated(Gun g, Vector3 at) => blast ??= at;

        [SetUp]
        public void Begin()
        {
            collision = new SettingsScope<int>(() => Settings.ShipCollision, v => Settings.ShipCollision = v);
            arming = new SettingsScope<bool>(() => Settings.BombArming, v => Settings.BombArming = v);
            Settings.ShipCollision = SessionRules.CollisionOriginal;   // the fighters' +-1000 cubes
            blast = null;
            Gun.Detonated += OnDetonated;
        }

        [TearDown]
        public void CleanUp()
        {
            Gun.Detonated -= OnDetonated;
            arming?.Dispose();
            collision?.Dispose();
            foreach (var go in made) if (go != null) Object.DestroyImmediate(go);
            made.Clear();
        }

        Target Make(string name, Vector3 at, float radiusUnits, bool asteroid = false)
        {
            var go = new GameObject(name);
            made.Add(go);
            go.transform.position = at;
            var t = go.AddComponent<Target>();
            t.radius = radiusUnits * M;
            t.isShip = !asteroid;
            t.isAsteroid = asteroid;
            t.hostileToPlayer = !asteroid;
            t.hp = t.maxHp = 100f;
            return t;
        }

        /// <summary>An Extinctor fired from the origin along +x (no forward offset), flown up to 'ms' in 16 ms frames until it
        /// goes off; returns the gun.</summary>
        Gun Fire(List<Target> targets, float ms = 3000f, System.Action<Gun> setup = null)
        {
            var gun = new Gun(Database.Shared.Item(Extinctor), Vector3.zero, true);
            gun.owner = Make("player", Vector3.zero, 1000f);
            setup?.Invoke(gun);
            Assert.AreEqual(0, gun.TryFire(Vector3.zero, Quaternion.LookRotation(Vector3.right), false));
            for (float t = 0f; t < ms && blast == null; t += 16f) gun.Update(16f, targets, null);
            return gun;
        }

        [TestCase(0f, 7f, false, true)]
        [TestCase(0f, 7f, true, false)]
        [TestCase(1999f / 7f, 7f, true, false)]
        [TestCase(2000f / 7f, 7f, true, true)]
        public void ArmedAfterTheDistance(float ageMs, float speed, bool arming, bool armed) =>
            Assert.AreEqual(armed, Gun.BombArmed(ageMs, speed, arming));

        [Test]
        public void TheOriginalRuleIgnitesInAFightersCubeAtLaunch()
        {
            Settings.BombArming = false;
            var fighter = Make("fighter", new Vector3(1200f * M, 0f, 0f), 1000f);   // its cube from 200 units ahead
            Fire(new List<Target> { fighter });
            Assert.IsTrue(blast.HasValue, "it went off");
            Assert.Less(blast.Value.x / M, 400f, "at the cube's near side, just ahead of the ship");
        }

        [Test]
        public void ArmedItIgnitesOnlyPastTheArmingDistance()
        {
            Settings.BombArming = true;
            var fighter = Make("fighter", new Vector3(1200f * M, 0f, 0f), 1000f);   // the cube reaches 2200 units out
            Fire(new List<Target> { fighter });
            Assert.IsTrue(blast.HasValue, "still in the cube once armed");
            Assert.GreaterOrEqual(blast.Value.x / M, Gun.BombArmingUnits - 1f);
        }

        [Test]
        public void ArmedItFliesThroughACubeItLeavesBeforeArming()
        {
            Settings.BombArming = true;
            var fighter = Make("fighter", new Vector3(600f * M, 0f, 0f), 1000f);   // the cube ends 1600 units out
            var gun = Fire(new List<Target> { fighter }, 1000f);
            Assert.IsFalse(blast.HasValue, "no contact");
            Assert.IsTrue(gun.BombInFlight, "flying on");
        }

        [Test]
        public void ArmingStillKillsAsteroidsAndASecondPressStillDetonates()
        {
            Settings.BombArming = true;
            var rock = Make("asteroid", new Vector3(500f * M, 0f, 0f), 300f, true);
            Target struck = null;
            var gun = Fire(new List<Target> { rock }, 160f, g => g.Hit += (_, t, __) => struck ??= t);
            Assert.AreSame(rock, struck, "the asteroid dies on contact, as in the original");
            Assert.IsFalse(blast.HasValue);
            gun.Detonate();
            Assert.IsTrue(blast.HasValue, "the second press");
        }
    }
}
