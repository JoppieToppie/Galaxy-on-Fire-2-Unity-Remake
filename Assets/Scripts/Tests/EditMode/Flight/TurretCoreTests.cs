// TurretCoreTests.cs
// Feature: CLAUDE.md "Weapons" > Turrets, the shared turret (Flight.TurretCore, TurretTargeting) the player's turret items,
// the NPC turret objects, the sentry guns and the hangar use. From the original (TurretCore.cs's header):
//   the aim (PlayerTurret::handleRotation / handleAutoTurret): steps of rate x dt toward the point, never a snap; an item
//     turret yaws at attr17 x 1.5 / 200 x 2 pi / 4096 rad per ms, pitches within [-70, +500] x 2 pi / 4096; an NPC turret
//     object within [-100, +600]; aligned within +-0.05 of the barrel's axes, the point in front
//   the pick: the player's auto turrets and the sentries (Retarget) every 3000 ms or at once when the target is lost, the
//     unreachable one skipped; the NPC turrets (Timed) only after more than 3000 ms, the unreachable one as a fallback
//   the shots: an item turret's from the gun + (0, 0, 300) (48: x +-80 alternating, 181: z -300) in the gun's frame, an
//     NPC turret object's 600 units ahead of its barrel, a sentry's from its muzzle
// Placement: WeaponMount.TurretTurn on the mount's position (TurretMountTests covers the turn itself).

using System.Collections.Generic;
using GoF2Remake.Data;
using GoF2Remake.Flight;
using NUnit.Framework;
using UnityEngine;

namespace GoF2Remake.Tests
{
    public class TurretCoreTests
    {
        const float M = Gun.MetersPerUnit;
        readonly List<GameObject> made = new List<GameObject>();

        [TearDown]
        public void CleanUp()
        {
            foreach (var go in made) if (go != null) Object.DestroyImmediate(go);
            made.Clear();
        }

        GameObject New(string name)
        {
            var go = new GameObject(name);
            made.Add(go);
            return go;
        }

        /// <summary>A turret model like the assemblies': root -> "pivot" -> "x_gun" (barrel along +Z).</summary>
        (Transform root, Transform pivot, Transform gun) Model()
        {
            var root = New("turret").transform;
            var pivot = new GameObject("pivot").transform;
            pivot.SetParent(root, false);
            var gun = new GameObject("turret_002_gun").transform;
            gun.SetParent(pivot, false);
            gun.localPosition = new Vector3(0f, 1f, 0f);
            return (root, pivot, gun);
        }

        Target NewTarget(Vector3 position, int hp = 100, bool cloaked = false)
        {
            var t = New("target").AddComponent<Target>();
            t.transform.position = position;
            t.isShip = true;
            t.hp = t.maxHp = hp;
            t.cloaked = cloaked;
            return t;
        }

        static ItemData Item(int index, int look, int yawAttr17 = -1)
        {
            var it = new ItemData { index = index, categoryId = 8, lookIndex = look };
            if (yawAttr17 >= 0) { it.attrKeys = new[] { 17 }; it.attrValues = new[] { yawAttr17 }; }
            return it;
        }

        // ---- finding the parts, placement ------------------------------------------------------------------------------

        [Test]
        public void FindsThePivotAndTheLastGun()
        {
            var (root, pivot, gun) = Model();
            var second = new GameObject("other_gun").transform;
            second.SetParent(pivot, false);
            Assert.IsTrue(TurretCore.FindNodes(root, out var p, out var g));
            Assert.AreSame(pivot, p);
            Assert.AreSame(second, g, "the last *_gun child, like the original loop");
            Assert.IsFalse(TurretCore.FindNodes(New("plain").transform, out _, out _), "a model without pivot / gun has none");
        }

        [Test]
        public void PlacedOnTheMountWithItsTurn()
        {
            var t = New("t").transform;
            var mount = new WeaponMount { slotType = 2, position_engine = new[] { 100, 200, 300 }, turretUp = new[] { 1f, 0f, 0f } };
            TurretCore.PlaceOnMount(t, mount, Quaternion.Euler(0f, 180f, 0f));
            Assert.AreEqual(WeaponSystem.MountToLocal(mount), t.localPosition, "the mount's position (ship-relative (-x, y, z) x 0.05)");
            Assert.Less(Vector3.Distance(Vector3.left, t.localRotation * Vector3.up), 1e-4f, "its base faces the mount's turretUp");
            Assert.Less(Quaternion.Angle(mount.TurretTurn * Quaternion.Euler(0f, 180f, 0f), t.localRotation), 1e-3f, "the mount's turn on top of the model's");
        }

        [TestCase("freighter", true)]
        [TestCase("frigate", true)]
        [TestCase("cruiser", true)]
        [TestCase("CARRIER", true)]
        [TestCase("heavy_fighter", false)]
        [TestCase("heavy_privateer", false)]
        [TestCase("hauler", false)]
        [TestCase("interceptor", false)]
        [TestCase(null, false)]
        public void SurfaceAimIsReservedForFourTargetClasses(string shipClass, bool expected)
        {
            Assert.AreEqual(expected, TurretTargeting.UsesHullSurface(shipClass));
        }

        [Test]
        public void UnclassifiedShipAimsAtCollisionCentreInsteadOfOriginOrNearestFace()
        {
            var t = NewTarget(new Vector3(100f, 20f, 300f));
            t.boxes = new[] { new Bounds(new Vector3(10f, 0f, 0f), new Vector3(4f, 4f, 4f)),
                              new Bounds(new Vector3(20f, 0f, 0f), new Vector3(4f, 4f, 4f)) };
            t.transform.rotation = Quaternion.Euler(0f, 90f, 0f);
            var expected = t.transform.TransformPoint(new Vector3(15f, 0f, 0f));
            Assert.Less(Vector3.Distance(expected, TurretTargeting.CombatAimPoint(t, Vector3.zero)), 1e-4f);
            Assert.Less(Vector3.Distance(expected, TurretTargeting.CombatAimPoint(t, Vector3.one * 500f)), 1e-4f);
        }

        // ---- aim ----------------------------------------------------------------------------------------------------

        [Test]
        public void AnItemTurretsAimFollowsItsItem()
        {
            var (_, pivot, gun) = Model();
            var aim = TurretCore.ItemAim(pivot, gun, Item(48, 48, 120), false);
            Assert.AreEqual(120f * 1.5f / 200f * TurretAim.RadPerMs, aim.yawRatePerMs, 1e-9f, "attr17 x 1.5 / 200 x 2 pi / 4096");
            Assert.AreEqual(TurretAim.RadPerMs, aim.pitchRatePerMs, 1e-9f);
            Assert.AreEqual(500f * TurretAim.RadPerMs, aim.pitchMax, 1e-6f, "44 deg up");
            Assert.AreEqual(-70f * TurretAim.RadPerMs, aim.pitchMin, 1e-6f, "6 deg down");
            Assert.AreEqual(100f * 1.5f / 200f * TurretAim.RadPerMs, TurretCore.ItemAim(pivot, gun, Item(47, 47), false).yawRatePerMs, 1e-9f,
                "attr 17 defaults to 100");
            Assert.AreEqual(Mathf.PI, TurretCore.ItemAim(pivot, gun, Item(47, 47), true).Yaw, 1e-6f, "a gun backwards in its pivot starts turned to the nose");
        }

        [Test]
        public void AnNpcTurretObjectsPitchRange()
        {
            var (_, pivot, gun) = Model();
            var aim = TurretCore.StaticAim(pivot, gun);
            Assert.AreEqual(600f * TurretAim.RadPerMs, aim.pitchMax, 1e-6f, "52.7 deg up");
            Assert.AreEqual(-100f * TurretAim.RadPerMs, aim.pitchMin, 1e-6f, "8.8 deg down");
            Assert.AreEqual(52.7f, aim.pitchMax * Mathf.Rad2Deg, 0.05f);
        }

        [Test]
        public void StepsTowardAPointThenAligns()
        {
            var (root, pivot, gun) = Model();
            var core = TurretCore.Wrap(root, pivot, gun, TurretCore.StaticAim(pivot, gun), TurretCore.ShotFrom.AheadOfBarrel);
            var point = gun.position + new Vector3(50f, 0f, 50f);   // 45 deg to the right
            Assert.IsFalse(core.StepToward(point, 33f), "not at once");
            Assert.AreEqual(33f * TurretAim.RadPerMs, core.Aim.Yaw, 1e-6f, "one step of rate x dt");
            int steps = 0;
            while (!core.StepToward(point, 33f) && steps < 200) steps++;
            Assert.Less(steps, 200, "it gets there");
            float x = Vector3.Dot((point - gun.position).normalized, gun.right);
            Assert.LessOrEqual(Mathf.Abs(x), 0.05f, "aligned within the dead zone");
        }

        [Test]
        public void APointBeyondThePitchLimitIsOutOfReach()
        {
            var (root, pivot, gun) = Model();
            var core = TurretCore.Wrap(root, pivot, gun, TurretCore.StaticAim(pivot, gun), TurretCore.ShotFrom.AheadOfBarrel);
            var below = gun.position + new Vector3(0f, -100f, 30f);   // far under the 8.8 deg limit
            bool hit = false;
            for (int i = 0; i < 100 && !hit; i++) { core.StepToward(below, 33f); hit = core.Aim.LimitHit; }
            Assert.IsTrue(hit);
            Assert.AreEqual(-100f * TurretAim.RadPerMs, core.Aim.Pitch, 1e-6f, "held at the limit");
        }

        [Test]
        public void LeadServoUsesTheActualBarrelWhenItFacesBackwardsInItsPivot()
        {
            using (new SettingsScope<bool>(() => Settings.TurretLeadAim, v => Settings.TurretLeadAim = v))
            {
                Settings.TurretLeadAim = true;
                var (_, pivot, gun) = Model();
                gun.localRotation = Quaternion.Euler(0f, 180f, 0f);
                var aim = new TurretAim(pivot, gun);
                var point = gun.position + Vector3.back * 100f;
                Assert.IsTrue(aim.Step(point, 16f));
                Assert.AreEqual(0f, aim.Yaw, 1e-4f);
                Assert.IsFalse(aim.LimitHit);
            }
        }

        [Test]
        public void LeadServoDoesNotRejectTargetWhileYawIsStillTurning()
        {
            using (new SettingsScope<bool>(() => Settings.TurretLeadAim, v => Settings.TurretLeadAim = v))
            {
                Settings.TurretLeadAim = true;
                var (_, pivot, gun) = Model();
                var aim = new TurretAim(pivot, gun) { pitchMin = -0.1f, pitchMax = 0.8f };
                Vector3 point = gun.position + new Vector3(0f, 20f, -100f);
                Assert.IsFalse(aim.Step(point, 16f));
                Assert.IsFalse(aim.LimitHit);
                bool aligned = false;
                for (int i = 0; i < 300 && !aligned; i++) aligned = aim.Step(point, 16f);
                Assert.IsTrue(aligned);
            }
        }

        // ---- shots --------------------------------------------------------------------------------------------------

        [Test]
        public void ItemTurretShotOffsets()
        {
            Assert.AreEqual(new Vector3(0f, 0f, 300f), TurretCore.ShotOffsetUnits(Item(47, 47), null));
            Assert.AreEqual(new Vector3(80f, 0f, 300f), TurretCore.ShotOffsetUnits(Item(48, 48), null));
            Assert.AreEqual(new Vector3(0f, 0f, -300f), TurretCore.ShotOffsetUnits(Item(181, 181), null));
            Assert.AreEqual(170f, TurretCore.MuzzleUnits(Item(48, 48), null));
            Assert.AreEqual(147f, TurretCore.MuzzleUnits(Item(224, 224), null));
            Assert.AreEqual(200f, TurretCore.MuzzleUnits(Item(300, 300), null), "others 200");
            var fx = ScriptableObject.CreateInstance<WeaponFx>();
            try
            {
                fx.turretMuzzleUnits = 90f;
                fx.turretShotUnits = 120f;
                Assert.AreEqual(90f, TurretCore.MuzzleUnits(Item(48, 48), fx), "a mod model's own muzzle");
                Assert.AreEqual(new Vector3(0f, 0f, 120f), TurretCore.ShotOffsetUnits(Item(48, 48), fx), "and shot start");
            }
            finally { Object.DestroyImmediate(fx); }
        }

        [Test]
        public void ShotOriginsByOwner()
        {
            var (root, pivot, gun) = Model();
            root.localScale = Vector3.one * 6f;   // a capital ship's turret object
            var core = TurretCore.Wrap(root, pivot, gun, TurretCore.StaticAim(pivot, gun), TurretCore.ShotFrom.AheadOfBarrel);
            Assert.Less(Vector3.Distance(gun.position + gun.forward * 600f * M, core.ShotOrigin(Vector3.zero)), 1e-4f,
                "an NPC turret object: 600 units ahead of the barrel, not scaled");
            core.shotFrom = TurretCore.ShotFrom.GunFrame;
            Assert.Less(Vector3.Distance(gun.TransformPoint(new Vector3(-80f, 0f, 300f) * M), core.ShotOrigin(new Vector3(80f, 0f, 300f))), 1e-4f,
                "an item turret: the offset in the gun's (scaled, mirrored x) frame");
            var muzzle = new GameObject("muzzle").transform;
            muzzle.SetParent(gun, false);
            muzzle.localPosition = new Vector3(0f, 0f, 250f) * M;
            core.SetMuzzle(muzzle);
            core.shotFrom = TurretCore.ShotFrom.Muzzle;
            Assert.Less(Vector3.Distance(muzzle.position, core.ShotOrigin(Vector3.zero)), 1e-4f, "a sentry: its muzzle");
        }

        [Test]
        public void FiringAlongTheBarrelAndTheHammerheadsSides()
        {
            var (root, pivot, gun) = Model();
            var core = TurretCore.Wrap(root, pivot, gun, TurretCore.StaticAim(pivot, gun), TurretCore.ShotFrom.GunFrame);
            var item = new ItemData { index = 48, categoryId = 8, lookIndex = 48 };
            var g = new Gun(item, 10f, 100f, 15, 2000f, 20f);
            core.SetGun(g, null);
            core.alternateSides = true;
            core.shotOffsetUnits = new Vector3(80f, 0f, 300f);
            int b0 = core.Fire();
            Assert.GreaterOrEqual(b0, 0, "a ready gun fires");
            Assert.AreEqual(0f, core.SinceShotMs, "the animations' clock starts");
            Assert.Greater(Vector3.Dot(g.bullets[b0].velocity.normalized, gun.forward), 0.999f, "along the barrel");
            float x0 = gun.InverseTransformPoint(g.bullets[b0].position).x;
            g.reloadAcc = 1e6f;
            int b1 = core.Fire();
            Assert.GreaterOrEqual(b1, 0);
            float x1 = gun.InverseTransformPoint(g.bullets[b1].position).x;
            Assert.AreEqual(-x0, x1, 1e-3f, "the next shot from the other side (x +-80)");
            Assert.AreEqual(80f * M, Mathf.Abs(x0), 1e-3f);
            Assert.AreEqual(-1, core.Fire(), "not ready again at once");
            Assert.IsTrue(core.RecentlyFired);
            core.SinceShotMs = g.reloadMs + 51f;
            Assert.IsFalse(core.RecentlyFired, "still once a reload has passed without a shot");
        }

        // ---- the pick -----------------------------------------------------------------------------------------------

        [Test]
        public void RetargetPicksTheNearestInRangeAtOnce()
        {
            var near = NewTarget(new Vector3(0f, 0f, 1000f));
            var far = NewTarget(new Vector3(0f, 0f, 2000f));
            var outOfRange = NewTarget(new Vector3(0f, 0f, 60000f * M + 1f));
            var pick = new TurretTargeting(TurretTargeting.Rule.Retarget, 60000f, true);
            Assert.AreSame(near, pick.Update(16f, new List<Target> { far, outOfRange, near }, Vector3.zero));
            Assert.IsTrue(pick.JustPicked);
            Assert.IsNull(new TurretTargeting(TurretTargeting.Rule.Retarget, 60000f).Update(16f, new List<Target> { outOfRange }, Vector3.zero),
                "60000 units is the reach");
        }

        [TestCase(TurretTargeting.Rule.Retarget)]
        [TestCase(TurretTargeting.Rule.Timed)]
        public void TurretRequiresSensorContactToAcquireAndRetain(TurretTargeting.Rule rule)
        {
            var hidden = NewTarget(new Vector3(0f, 0f, 100f));
            var visible = NewTarget(new Vector3(0f, 0f, 200f));
            var outsideWeaponRange = NewTarget(new Vector3(0f, 0f, 2600f));
            var pool = new List<Target> { hidden, visible, outsideWeaponRange };
            var pick = new TurretTargeting(rule, 50000f) { Sensor = t => t != hidden };
            Assert.AreSame(visible, pick.Update(3001f, pool, Vector3.zero));
            pick.Sensor = t => t == outsideWeaponRange;
            Assert.IsNull(pick.Update(16f, pool, Vector3.zero), "loss of contact is immediate even in timed mode; sensor does not extend gun range");
            pick.Sensor = t => true;
            Assert.AreSame(hidden, pick.Update(3001f, pool, Vector3.zero));
        }

        [TestCase(false)]
        [TestCase(true)]
        public void HudLeadSamplesAtRenderRateAndResetsOnDiscontinuity(bool remote)
        {
            var motion = new TurretTargeting.HudLeadMotion();
            Assert.IsFalse(motion.SamplePose(Vector3.zero, 0d, remote));
            Assert.IsTrue(motion.SamplePose(Vector3.right, 0.01d, remote));
            Assert.AreEqual(100f, motion.Velocity.x, 0.001f);
            Assert.IsTrue(motion.TryLead(Vector3.forward * 100f, Vector3.zero, 200f, 2f, out var lead));
            Assert.Greater(lead.x, 0f);
            Assert.IsTrue(motion.SamplePose(Vector3.zero, 0.02d, remote));
            Assert.Less(motion.Velocity.x, 0f, "direction reversal before the old 80 ms interval");
            Assert.IsTrue(motion.TryLead(Vector3.forward * 100f, Vector3.zero, 200f, 2f, out lead));
            Assert.Less(lead.x, 0f);
            Assert.IsFalse(motion.SamplePose(Vector3.right * 1000f, 0.03d, remote));
            Assert.IsFalse(motion.TryLead(Vector3.forward * 100f, Vector3.zero, 200f, 2f, out _));
        }

        [Test]
        public void HudLeadAllowsLegalPvpEvenBeforeAggressionAndRejectsProtectedPilots()
        {
            var t = NewTarget(Vector3.forward * 100f);
            t.netPilot = true;
            t.hostileToPlayer = false;
            t.playerProof = false;
            Assert.IsTrue(TurretTargeting.HudLeadEligible(t), "legal neutral PvP target");
            t.playerProof = true;
            Assert.IsFalse(TurretTargeting.HudLeadEligible(t), "squadmate or PvP disabled");
            t.playerProof = false;
            t.cloaked = true;
            Assert.IsFalse(TurretTargeting.HudLeadEligible(t));
            t.cloaked = false;
            t.untargetable = true;
            Assert.IsFalse(TurretTargeting.HudLeadEligible(t));
        }

        [Test]
        public void RetargetDropsALostTargetAtOnceAndSkipsTheUnreachable()
        {
            var a = NewTarget(new Vector3(0f, 0f, 100f));
            var b = NewTarget(new Vector3(0f, 0f, 200f));
            var pool = new List<Target> { a, b };
            var pick = new TurretTargeting(TurretTargeting.Rule.Retarget, 60000f, true) { Candidate = t => t.Alive && !t.cloaked };
            Assert.AreSame(a, pick.Update(16f, pool, Vector3.zero));
            a.cloaked = true;
            Assert.AreSame(b, pick.Update(16f, pool, Vector3.zero), "a cloaked target is replaced at once");
            a.cloaked = false;
            pick.Current = a;
            pick.LimitHit(16f);
            Assert.IsNull(pick.Current, "out of the turret's reach: dropped");
            Assert.AreSame(b, pick.Update(16f, pool, Vector3.zero), "the next pick skips it");
            pool.Remove(b);
            Assert.IsNull(pick.Update(3001f, pool, Vector3.zero), "nothing else: no target ...");
            Assert.AreSame(a, pick.Update(3001f, pool, Vector3.zero), "... and the unreachable one is forgotten");
        }

        [Test]
        public void TimedPicksOnlyAfterMoreThanThreeSeconds()
        {
            var a = NewTarget(new Vector3(0f, 0f, 100f));
            var pool = new List<Target> { a };
            var pick = new TurretTargeting(TurretTargeting.Rule.Timed, 50000f);
            Assert.IsNull(pick.Update(3000f, pool, Vector3.zero), "the first pick after 3 s ...");
            Assert.AreSame(a, pick.Update(1f, pool, Vector3.zero), "... once more than 3000 ms passed");
            a.hp = 0;
            Assert.AreSame(a, pick.Update(100f, pool, Vector3.zero), "kept until the next pick (handleTurret)");
        }

        [Test]
        public void TimedFallsBackToTheUnreachableOne()
        {
            var a = NewTarget(new Vector3(0f, 0f, 100f));
            var b = NewTarget(new Vector3(0f, 0f, 300f));
            var pool = new List<Target> { a, b };
            var pick = new TurretTargeting(TurretTargeting.Rule.Timed, 50000f);
            Assert.AreSame(a, pick.Update(3001f, pool, Vector3.zero));
            pick.LimitHit(16f);
            Assert.AreSame(a, pick.Current, "kept until the next pick");
            Assert.AreSame(b, pick.Update(3001f, pool, Vector3.zero), "another one first");
            pool.Remove(b);
            pick.Current = a;
            pick.LimitHit(16f);
            Assert.AreSame(a, pick.Update(3001f, pool, Vector3.zero), "only it left: picked anyway");
        }

        [Test]
        public void TheMeasureSeesTheBestSoFar()
        {
            // A turret hunter's weighting (CapitalShips): x0.3 only once nearer than the best so far, so a far turret listed
            // after a near hull isn't taken.
            var hull = NewTarget(new Vector3(0f, 0f, 100f));
            var turret = NewTarget(new Vector3(0f, 0f, 200f));
            var pick = new TurretTargeting(TurretTargeting.Rule.Timed, 50000f)
            {
                Measure = (t, from, best) => { float d = (t.transform.position - from).magnitude; return t == turret && d < best ? d * 0.3f : d; },
            };
            Assert.AreSame(hull, pick.Update(3001f, new List<Target> { hull, turret }, Vector3.zero), "listed after the nearer hull");
            Assert.AreSame(turret, pick.Update(3001f, new List<Target> { turret, hull }, Vector3.zero), "listed first: weighted");
        }

        [Test]
        public void AccelerationLeadPredictsAReachableBoundedIntercept()
        {
            var relative = new Vector3(0f, 0f, 1000f);
            var velocity = new Vector3(20f, 0f, 0f);
            var acceleration = new Vector3(60f, 0f, 0f);
            var linear = TurretTargeting.PredictIntercept(relative, velocity, Vector3.zero, 800f, 2f);
            var predicted = TurretTargeting.PredictIntercept(relative, velocity, acceleration, 800f, 2f);
            Assert.Greater(predicted.x, linear.x);
            float time = predicted.magnitude / 800f;
            float horizon = Mathf.Min(time, 1.25f);
            Assert.Less(Vector3.Distance(predicted, relative + velocity * time + acceleration * (0.5f * horizon * horizon)), 0.1f);
            Assert.LessOrEqual(Vector3.Distance(predicted, linear), 150f);
        }

        [Test]
        public void PredictionRejectsUnreachableAndExpiredIntercepts()
        {
            var relative = new Vector3(0f, 0f, 1000f);
            Assert.AreEqual(relative, TurretTargeting.PredictIntercept(relative, Vector3.forward * 900f, Vector3.right * 60f, 800f, 3f));
            Assert.AreEqual(relative, TurretTargeting.PredictIntercept(relative, Vector3.right * 40f, Vector3.right * 60f, 800f, 0.5f));
            Assert.AreEqual(relative, TurretTargeting.PredictIntercept(relative, Vector3.right * 40f, Vector3.right * 60f, 0f, 3f));
        }

        [Test]
        public void TheLeadPointIsFifteenHundredUnitsAhead()
        {
            var t = NewTarget(new Vector3(10f, 0f, 0f));
            t.transform.rotation = Quaternion.LookRotation(Vector3.up);
            Assert.Less(Vector3.Distance(new Vector3(10f, 1500f * M, 0f), TurretTargeting.LeadPoint(t)), 1e-4f);
        }
    }
}
