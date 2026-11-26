// BeamHullTests.cs
// Feature: CLAUDE.md "Special weapons" (docs/notes/weapons.md; Gun.AimedBeamHit, #192). Expected behaviour (the original,
// Gun::shootAt: the beam is parked at its target's centre, inside its cube): an aimed beam hits its target even when the
// line to the centre passes through a gap in the hull (the Midorian freighter's centre lies between its four parts, so
// about 6 % of the angles did nothing); the point is on the hull past the centre; a beam fired straight ahead past a hull
// still misses it, and a nearer hull in the way still takes the shot.

using System.Collections.Generic;
using GoF2Remake.Data;
using GoF2Remake.Flight;
using NUnit.Framework;
using UnityEngine;

namespace GoF2Remake.Tests
{
    public class BeamHullTests : SandboxTest
    {
        readonly List<GameObject> made = new List<GameObject>();

        System.IDisposable collision;

        [SetUp]
        public void ExactHulls()
        {
            collision = new SettingsScope<int>(() => Settings.ShipCollision, v => Settings.ShipCollision = v);   // the player's own back after
            Settings.ShipCollision = SessionRules.CollisionExact;
        }

        [TearDown]
        public void CleanUp()
        {
            collision?.Dispose();
            foreach (var go in made) if (go != null) Object.DestroyImmediate(go);
            made.Clear();
        }

        GameObject Cube(Transform parent, Vector3 at, Vector3 size)
        {
            var c = GameObject.CreatePrimitive(PrimitiveType.Cube);
            made.Add(c);
            c.transform.SetParent(parent, false);
            c.transform.localPosition = at;
            c.transform.localScale = size;
            return c;
        }

        /// <summary>A target whose hull is two blocks 10 m apart along x, its centre in the gap between them.</summary>
        Target GappedHull(Vector3 at)
        {
            var root = new GameObject("freighter");
            made.Add(root);
            root.transform.position = at;
            Cube(root.transform, new Vector3(-10f, 0f, 0f), new Vector3(10f, 10f, 40f));
            Cube(root.transform, new Vector3(10f, 0f, 0f), new Vector3(10f, 10f, 40f));
            var target = root.AddComponent<Target>();
            Assert.IsNotNull(HullCollision.Attach(root, target, null), "the blocks are a hull");
            Physics.SyncTransforms();
            return target;
        }

        /// <summary>A target whose hull is one solid block, 40 x 10 x 40 m.</summary>
        Target Block(Vector3 at)
        {
            var root = new GameObject("block");
            made.Add(root);
            root.transform.position = at;
            Cube(root.transform, Vector3.zero, new Vector3(40f, 10f, 40f));
            var target = root.AddComponent<Target>();
            Assert.IsNotNull(HullCollision.Attach(root, target, null));
            Physics.SyncTransforms();
            return target;
        }

        static Gun Beam() => new Gun(Database.Shared.Item(9), Vector3.zero, false);

        /// <summary>One shot from 'from' toward 'dir', 'aim' as the auto-aim target: what it hit and where, null = nothing.</summary>
        static (Target target, Vector3 point) Shoot(Gun gun, Vector3 from, Vector3 dir, Target aim, List<Target> targets)
        {
            gun.AutoAim = () => aim;
            Target hit = null;
            var point = Vector3.zero;
            gun.Hit += (_, t, p) => { hit = t; point = p; };
            Assert.AreEqual(0, gun.TryFire(from, Quaternion.LookRotation(dir), false));
            gun.Update(16f, targets, null);
            return (hit, point);
        }

        static Gun PrecisionBeam() => new Gun(new ItemData
        {
            index = 11, lookIndex = 11, categoryId = 0,
            attrKeys = new[] { Gun.BeamReachAttr }, attrValues = new[] { 280000 }
        }, Vector3.zero, false);

        [Test]
        public void NarrowBeamExplicitReachControlsAcquisitionAndManualFire()
        {
            var gun = PrecisionBeam();
            Assert.IsTrue(gun.isBeam);
            Assert.AreEqual(0f, gun.beamRadius);
            Assert.AreEqual(14000f, gun.beamReach * Gun.MetersPerUnit, 0.01f);
            Assert.IsTrue(WeaponSystem.BeamTargetInRange(Vector3.forward * 13999f, true, gun));
            Assert.IsTrue(WeaponSystem.BeamTargetInRange(Vector3.forward * 13999f, false, gun));
            Assert.IsFalse(WeaponSystem.BeamTargetInRange(Vector3.forward * 14001f, true, gun));
            Assert.IsFalse(WeaponSystem.BeamTargetInRange(Vector3.forward * 14001f, false, gun));
            var stock = Beam();
            Assert.IsFalse(stock.hasCustomBeamReach);
            Assert.IsTrue(WeaponSystem.BeamTargetInRange(Vector3.forward * 2999f, true, stock));
            Assert.IsFalse(WeaponSystem.BeamTargetInRange(Vector3.forward * 3001f, true, stock));

            var near = Block(Vector3.forward * 13900f);
            var (hit, _) = Shoot(gun, Vector3.zero, Vector3.forward, null, new List<Target> { near });
            Assert.AreSame(near, hit, "manual fire must reach a narrow-beam target near 14 km");
        }

        [Test]
        public void NarrowBeamCannotAutoAimBeyondItsExplicitReach()
        {
            var gun = PrecisionBeam();
            var far = Block(Vector3.forward * 14100f);
            var (hit, _) = Shoot(gun, Vector3.zero, Vector3.forward, far, new List<Target> { far });
            Assert.IsNull(hit);
            Assert.IsNull(gun.BeamTarget);
            Assert.AreEqual(14000f, gun.BeamLengthUnits * Gun.MetersPerUnit, 0.01f);
        }

        [Test]
        public void AnAimedBeamThroughAGapInTheHullHitsItsTarget()
        {
            var target = GappedHull(new Vector3(0f, 0f, 500f));
            var targets = new List<Target> { target };
            // Down the gap from above (its line to the centre meets neither block).
            var from = new Vector3(0f, 200f, 500f);
            Assert.IsFalse(HullCollision.Cast(from, target.transform.position, 0.05f, t => t == target, out _, out _),
                           "the line to the centre passes the gap");
            var (hit, point) = Shoot(Beam(), from, Vector3.down, target, targets);
            Assert.AreSame(target, hit, "the aimed beam hits");
            Assert.AreEqual(500f, point.z, 0.01f);
            Assert.LessOrEqual(point.y, 0.01f, "at its centre or past it, not short of the hull");
        }

        [Test]
        public void AnAimedBeamOnTheHullHitsWhereItMeetsIt()
        {
            var target = Block(new Vector3(0f, 0f, 500f));
            var (hit, point) = Shoot(Beam(), new Vector3(0f, 0f, 300f), Vector3.forward, target, new List<Target> { target });
            Assert.AreSame(target, hit);
            Assert.Less(point.z, 500f, "at the hull's near end, not the centre");
        }

        [Test]
        public void AStraightBeamPastTheHullMissesAndANearerHullTakesTheShot()
        {
            var target = GappedHull(new Vector3(0f, 0f, 500f));
            var targets = new List<Target> { target };
            var (miss, _) = Shoot(Beam(), new Vector3(0f, 200f, 0f), Vector3.forward, null, targets);
            Assert.IsNull(miss, "not aimed, not on the hull: no hit");

            var nearer = Block(new Vector3(0f, 100f, 500f));   // straight between the gun and the target
            targets.Add(nearer);
            var (hit, point) = Shoot(Beam(), new Vector3(0f, 300f, 500f), Vector3.down, target, targets);
            Assert.AreSame(nearer, hit, "the nearer hull in the way");
            Assert.AreEqual(105f, point.y, 0.1f, "on its top");
        }
    }
}
