// ModShipAnimatorTests.cs
// ships.json "animations" (Modding.ModShipAnimator): a mod ship's model plays its idle clip on a loop and Chorus flight's
// drift clips by name (start once, then the loop; the end once, then idle); a clip the model lacks is skipped.

using GoF2Remake.Data;
using GoF2Remake.Modding;
using NUnit.Framework;
using UnityEngine;

namespace GoF2Remake.Tests
{
    public class ModShipAnimatorTests
    {
        static GameObject Model(params string[] clips)
        {
            var root = new GameObject("ship");
            var hull = new GameObject("hull"); hull.transform.SetParent(root.transform, false);
            var anim = hull.AddComponent<Animation>();
            foreach (var name in clips)
            {
                var clip = new AnimationClip { legacy = true, name = name };
                clip.SetCurve("", typeof(Transform), "localPosition.x", AnimationCurve.Linear(0f, 0f, 1f, 1f));
                anim.AddClip(clip, name);
            }
            return root;
        }

        static ShipAnimations Spec() => new ShipAnimations
        {
            idle = "idle", drift = new ShipStateAnimation { start = "driftStart", loop = "driftLoop", end = "driftEnd" },
            travel = new ShipStateAnimation { start = "travelStart", loop = "travelLoop", end = "travelEnd" },
            steerLeft = "steerL", steerRight = "steerR", thrust = "thrusting", brake = "brake",
            weapons = new ShipStateAnimation { start = "deploy", loop = "deployed", end = "undeploy" },
            dodgeLeft = new ShipStateAnimation { start = "rollStartL", end = "rollEndL" },
            driftDodgeLeft = "driftDodgeL",
        };

        [Test]
        public void TheIdleLoopsAndTheDriftPlaysItsStartThenItsEnd()
        {
            var root = Model("idle", "driftStart", "driftLoop", "driftEnd");
            try
            {
                var a = ModShipAnimator.Attach(root, Spec());
                Assert.IsNotNull(a);
                Assert.IsTrue(a.anim.IsPlaying("idle"));
                Assert.AreEqual(WrapMode.Loop, a.anim["idle"].wrapMode);
                Assert.AreEqual(WrapMode.Loop, a.anim["driftLoop"].wrapMode);
                Assert.AreEqual(WrapMode.ClampForever, a.anim["driftStart"].wrapMode);

                a.SetDrift(true);
                Assert.IsTrue(a.Drifting);
                Assert.IsTrue(a.anim.IsPlaying("driftStart"));
                a.SetDrift(false);
                Assert.IsFalse(a.Drifting);
                Assert.IsTrue(a.anim.IsPlaying("driftEnd"));
            }
            finally { Object.DestroyImmediate(root); }
        }

        [Test]
        public void MissingClipsAreSkipped()
        {
            var root = Model("idle", "driftLoop");   // no start, no end
            try
            {
                var a = ModShipAnimator.Attach(root, Spec());
                Assert.IsNull(a.drift[0]);
                Assert.IsNull(a.drift[2]);
                a.SetDrift(true);
                Assert.IsTrue(a.anim.IsPlaying("driftLoop"));
                a.SetDrift(false);
                Assert.IsTrue(a.anim.IsPlaying("idle"));
            }
            finally { Object.DestroyImmediate(root); }
        }

        [Test]
        public void TheTravelBoostPlaysLikeTheDrift()
        {
            var root = Model("idle", "travelStart", "travelLoop", "travelEnd");
            try
            {
                var a = ModShipAnimator.Attach(root, Spec());
                a.SetTravel(true);
                Assert.IsTrue(a.anim.IsPlaying("travelStart"));
                a.SetTravel(false);
                Assert.IsTrue(a.anim.IsPlaying("travelEnd"));
            }
            finally { Object.DestroyImmediate(root); }
        }

        [Test]
        public void PosesFollowTheInputOnALayerAbove()
        {
            var root = Model("idle", "steerL", "steerR", "thrusting", "brake");
            try
            {
                var a = ModShipAnimator.Attach(root, Spec());
                Assert.AreEqual(2, a.anim["steerL"].layer);
                Assert.IsFalse(a.anim["steerL"].enabled);
                for (int i = 0; i < 60; i++) a.SetPoses(-1f, 1f, 0f, 0.05f);   // three seconds full left, full thrust
                Assert.IsTrue(a.anim["steerL"].enabled);
                Assert.Greater(a.anim["steerL"].weight, 0.95f);
                Assert.Greater(a.anim["thrusting"].weight, 0.95f);
                Assert.IsFalse(a.anim["steerR"].enabled);
                Assert.IsFalse(a.anim["brake"].enabled);
                Assert.AreEqual(1f, a.anim["steerL"].normalizedTime, 1e-4f);   // the clip's last frame: the full pose
                Assert.IsTrue(a.anim.IsPlaying("idle"));                      // the state below keeps playing
            }
            finally { Object.DestroyImmediate(root); }
        }

        [Test]
        public void TheWeaponsComeOutWhileFiringAndGoBackAfter()
        {
            var root = Model("idle", "deploy", "deployed", "undeploy");
            try
            {
                var a = ModShipAnimator.Attach(root, Spec());
                Assert.AreEqual(1, a.anim["deploy"].layer);
                a.SetFiring(true, 10f);
                Assert.IsTrue(a.Armed);
                Assert.IsTrue(a.anim.IsPlaying("deploy"));
                Assert.IsTrue(a.anim.IsPlaying("idle"));      // the base layer keeps playing
                a.SetFiring(false, 12f);                      // within the 3 s
                Assert.IsTrue(a.Armed);
                a.SetFiring(false, 13.5f);
                Assert.IsFalse(a.Armed);
                Assert.IsTrue(a.anim.IsPlaying("undeploy"));
            }
            finally { Object.DestroyImmediate(root); }
        }

        [Test]
        public void TheDashRollsOnceAndOutOfADriftWithItsOwnClip()
        {
            var root = Model("idle", "rollStartL", "rollEndL", "driftDodgeL", "driftLoop");
            try
            {
                var a = ModShipAnimator.Attach(root, Spec());
                Assert.AreEqual(3, a.anim["rollStartL"].layer);
                Assert.AreEqual(WrapMode.Once, a.anim["rollStartL"].wrapMode);
                a.Dodge(-1);
                Assert.IsTrue(a.anim.IsPlaying("rollStartL"));
                a.SetDrift(true);
                a.Dodge(-1);
                Assert.IsTrue(a.anim.IsPlaying("driftDodgeL"));
                a.Dodge(1);                                   // no right-hand clips: nothing new plays
                Assert.IsTrue(a.anim.IsPlaying("driftLoop"));
            }
            finally { Object.DestroyImmediate(root); }
        }

        [Test]
        public void NoSpecOrNoAnimationLeavesTheModelAlone()
        {
            var root = Model();
            var bare = new GameObject("bare");
            try
            {
                Assert.IsNull(ModShipAnimator.Attach(root, null));
                Assert.IsNull(ModShipAnimator.Attach(bare, Spec()));
            }
            finally { Object.DestroyImmediate(root); Object.DestroyImmediate(bare); }
        }

        [Test]
        public void ShipsJsonAnimationsReachTheShipData()
        {
            var c = JsonUtility.FromJson<CustomShipData>(
                "{ \"animations\": { \"idle\": \"idle\", \"drift\": { \"start\": \"driftStart\", \"loop\": \"driftLoop\", \"end\": \"driftEnd\" } } }");
            Assert.AreEqual("idle", c.animations.idle);
            Assert.AreEqual("driftLoop", c.animations.drift.loop);
        }
    }
}
