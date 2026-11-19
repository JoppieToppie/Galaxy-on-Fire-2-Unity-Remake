// Es2ChaseRigTests.cs
// EVERSPACE 2's chase camera for free flight (Es2ChaseRig, ChaseCamera.PlaceEs2): the rotation lag of
// UMathLib::ESRInterpToDynamicTarget, the velocity offset's clamps and the boost's horizontal FOV.

using GoF2Remake.Flight;
using NUnit.Framework;
using UnityEngine;

namespace GoF2Remake.Tests
{
    public class Es2ChaseRigTests
    {
        static readonly float K = Mathf.Pow(10f, Es2ChaseRig.Stiffness);

        // A steady turn at w: the lag settles at w / ln(10^X) (an exponential follow of a ramp), ES2's default 0.087 s of it.
        [TestCase(30f)] [TestCase(60f)] [TestCase(144f)] [TestCase(240f)]
        public void SteadyTurnLagsByTheStiffnessTimeConstant(float fps)
        {
            float w = 120f, dt = 1f / fps;   // deg/s
            Quaternion ship = Quaternion.identity, cam = ship, last = ship;
            for (int i = 0; i < fps * 3f; i++)
            {
                ship = Quaternion.AngleAxis(w * dt, Vector3.up) * ship;
                cam = Es2ChaseRig.LagRotation(cam, last, ship, dt, K);
                last = ship;
            }
            float lag = Quaternion.Angle(cam, ship);
            Assert.AreEqual(w / Mathf.Log(K), lag, 0.6f, "the lag in degrees");
        }

        [Test]
        public void StoppingCatchesUpAndRollFollowsWithoutLevelling()
        {
            Quaternion ship = Quaternion.AngleAxis(40f, Vector3.forward), cam = Quaternion.identity, last = ship;
            for (int i = 0; i < 120; i++) { cam = Es2ChaseRig.LagRotation(cam, last, ship, 1f / 60f, K); last = ship; }
            Assert.Less(Quaternion.Angle(cam, ship), 0.01f, "the camera rolls with the ship: no horizon");
        }

        [Test]
        public void PausedOrStillLeavesTheRotation()
        {
            var a = Quaternion.Euler(10f, 20f, 30f);
            Assert.AreEqual(a, Es2ChaseRig.LagRotation(a, Quaternion.identity, Quaternion.identity, 0f, K));
            Assert.AreEqual(a, Es2ChaseRig.LagRotation(a, a, a, 1f / 60f, K));
        }

        [Test]
        public void TheFrameCapHoldsTheLagOnALongFrame()
        {
            // a 0.5 s hitch takes no more of the way than the 1/30 s cap
            Quaternion target = Quaternion.AngleAxis(90f, Vector3.up), cur = Quaternion.identity;
            var capped = Es2ChaseRig.LagRotation(cur, cur, target, 0.5f, K);
            var frame = Es2ChaseRig.LagRotation(cur, cur, target, Es2ChaseRig.MaxDt, K);
            Assert.AreEqual(0f, Quaternion.Angle(capped, frame), 1e-3f);
        }

        [Test]
        public void VelocityOffsetTrailsWithinEs2sClamps()
        {
            // 100 m/s forward: 5 m back; strafe 50 m/s right: 2.5 m left; clamps at 10 m back, 20 m aside, 20 m down, 3 m up
            Assert.AreEqual(new Vector3(0f, 0f, 30f - 5f), Es2ChaseRig.PivotGoal(new Vector3(0f, 0f, 100f), 30f, 1f));
            Assert.AreEqual(-2.5f, Es2ChaseRig.PivotGoal(new Vector3(50f, 0f, 0f), 30f, 1f).x, 1e-4f);
            Assert.AreEqual(30f - 10f, Es2ChaseRig.PivotGoal(new Vector3(0f, 0f, 1000f), 30f, 1f).z, 1e-4f);
            Assert.AreEqual(30f + 3f, Es2ChaseRig.PivotGoal(new Vector3(0f, 0f, -1000f), 30f, 1f).z, 1e-4f);
            Assert.AreEqual(-20f, Es2ChaseRig.PivotGoal(new Vector3(1000f, 0f, 0f), 30f, 1f).x, 1e-4f);
            Assert.AreEqual(3f, Es2ChaseRig.PivotGoal(new Vector3(0f, -1000f, 0f), 30f, 1f).y, 1e-4f);
            Assert.AreEqual(-20f, Es2ChaseRig.PivotGoal(new Vector3(0f, 1000f, 0f), 30f, 1f).y, 1e-4f);
            // a smaller rig trails proportionally
            Assert.AreEqual(15f - 2.5f, Es2ChaseRig.PivotGoal(new Vector3(0f, 0f, 100f), 15f, 0.5f).z, 1e-4f);
        }

        [Test]
        public void CameraHangsBehindThePivotAlongItsRotation()
        {
            var p = Es2ChaseRig.CameraPosition(new Vector3(0f, 0f, 30f), Quaternion.identity, 30f, 28f, 6f);
            Assert.AreEqual(new Vector3(0f, 6f, -28f), p);
            // the pivot turned 90 deg: the camera swings out round the point ahead
            p = Es2ChaseRig.CameraPosition(new Vector3(0f, 0f, 30f), Quaternion.AngleAxis(90f, Vector3.up), 30f, 28f, 0f);
            Assert.AreEqual(-58f, p.x, 1e-3f);
            Assert.AreEqual(30f, p.z, 1e-3f);
        }

        [Test]
        public void BoostWidensTheHorizontalViewBy15Degrees()
        {
            float v = 60f, wide = Es2ChaseRig.WidenVertical(v, Es2ChaseRig.BoostFov);
            float H(float vert) => 2f * Mathf.Atan(Mathf.Tan(vert * 0.5f * Mathf.Deg2Rad) * 16f / 9f) * Mathf.Rad2Deg;
            Assert.AreEqual(H(v) + 15f, H(wide), 1e-3f);
            Assert.AreEqual(v, Es2ChaseRig.WidenVertical(v, 0f), 1e-4f);
        }

        [Test]
        public void DollyKeepsTheShipsSizeAcrossTheFieldOfView()
        {
            // the ship's size on screen ~ 1 / (distance x tan(fov / 2)): the same at every FOV
            float Size(float fov) => 1f / (Es2ChaseRig.DollyArm(28f, 70f, fov) * Mathf.Tan(fov * 0.5f * Mathf.Deg2Rad));
            Assert.AreEqual(28f, Es2ChaseRig.DollyArm(28f, 70f, 70f), 1e-4f);
            Assert.AreEqual(Size(70f), Size(55f), 1e-5f);
            Assert.AreEqual(Size(70f), Size(95f), 1e-5f);
            Assert.Less(Es2ChaseRig.DollyArm(28f, 70f, 95f), 28f, "wider: the camera comes in");
        }

        [Test]
        public void TheFreeLookBlendEasesInAndOut()
        {
            Assert.AreEqual(0f, Es2ChaseRig.EaseInOut(0f, 2f));
            Assert.AreEqual(0.5f, Es2ChaseRig.EaseInOut(0.5f, 2f), 1e-5f);
            Assert.AreEqual(1f, Es2ChaseRig.EaseInOut(1f, 2f));
            Assert.AreEqual(0.125f, Es2ChaseRig.EaseInOut(0.25f, 2f), 1e-5f);
        }

        // ---- the shakes (Es2CameraShake) ----

        [Test]
        public void AShakeBlendsInAndOutOverItsDuration()
        {
            Assert.AreEqual(0.5f, Es2CameraShake.Weight(0.1f, 0.4f, 0.5f, 0.2f, 0.2f), 1e-5f, "half way through the blend in");
            Assert.AreEqual(1f, Es2CameraShake.Weight(0.25f, 0.25f, 0.5f, 0.2f, 0.2f), 1e-5f);
            Assert.AreEqual(0.5f, Es2CameraShake.Weight(0.4f, 0.1f, 0.5f, 0.2f, 0.2f), 1e-5f, "half way through the blend out");
        }

        [Test]
        public void AHitRunsOutWhileAReissuedShakeKeepsGoing()
        {
            var hit = Es2CameraShake.Hit();
            hit.Play(0.5f);
            Vector3 r = Vector3.zero, l = Vector3.zero;
            for (int i = 0; i < 13; i++) hit.Tick(1f / 60f, false, ref r, ref l);
            Assert.IsFalse(hit.Playing, "0.2 s");

            var boost = Es2CameraShake.Boost();
            for (int i = 0; i < 120; i++) { boost.Play(1f); boost.Tick(1f / 60f, false, ref r, ref l); }
            Assert.IsTrue(boost.Playing, "played every frame, a single instance lasts");
            for (int i = 0; i < 40; i++) boost.Tick(1f / 60f, false, ref r, ref l);
            Assert.IsFalse(boost.Playing, "then runs out over its 0.5 s");
            Assert.IsFalse(Es2CameraShake.Hit().Playing);
            var tiny = Es2CameraShake.Hit();
            tiny.Play(0.001f);
            Assert.IsFalse(tiny.Playing, "0.001 or less doesn't start");
        }

        [Test]
        public void AShakeStaysWithinItsAmplitudesAndTheCockpitScalesItsLocation()
        {
            var idle = Es2CameraShake.IdleFlight();
            idle.Play(1f);
            float maxLoc = 0f, maxCockpit = 0f;
            for (int i = 0; i < 600; i++)
            {
                idle.Play(1f);
                Vector3 r = Vector3.zero, l = Vector3.zero, rc = Vector3.zero, lc = Vector3.zero;
                idle.Tick(1f / 60f, false, ref r, ref l);
                maxLoc = Mathf.Max(maxLoc, Mathf.Abs(l.x), Mathf.Abs(l.y), Mathf.Abs(l.z));
                Assert.AreEqual(Vector3.zero, r, "no rotation in the idle shake");
            }
            Assert.LessOrEqual(maxLoc, 20f + 1e-3f);
            Assert.Greater(maxLoc, 5f);
            var cockpit = Es2CameraShake.IdleFlight();
            for (int i = 0; i < 600; i++)
            {
                cockpit.Play(1f);
                Vector3 r = Vector3.zero, l = Vector3.zero;
                cockpit.Tick(1f / 60f, true, ref r, ref l);
                maxCockpit = Mathf.Max(maxCockpit, l.magnitude);
            }
            Assert.LessOrEqual(maxCockpit, 20f * 0.05f * Mathf.Sqrt(3f) + 1e-3f, "FirstPersonScale 0.05");
        }

        [Test]
        public void HeavyHullsShakeLess()
        {
            Assert.AreEqual(1f, Es2CameraShake.ClassScale(0f));
            Assert.AreEqual(0.8f, Es2CameraShake.ClassScale(1f), 1e-5f);
        }

        [Test]
        public void InterpToMatchesUnrealsFInterpTo()
        {
            Assert.AreEqual(7.5f, Es2ChaseRig.InterpTo(0f, 15f, 0.25f, 2f), 1e-4f, "dt x speed of the way");
            Assert.AreEqual(15f, Es2ChaseRig.InterpTo(0f, 15f, 1f, 2f), 1e-4f, "dt x speed past 1 lands");
        }
    }
}
