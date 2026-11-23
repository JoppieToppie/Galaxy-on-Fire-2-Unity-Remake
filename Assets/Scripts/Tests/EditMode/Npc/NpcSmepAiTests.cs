// NpcSmepAiTests.cs
// Regression coverage for the mod opt-in boundary and SMEP's fallback firing safeguards.

using NUnit.Framework;
using GoF2Remake.World;

namespace GoF2Remake.Tests
{
    public class NpcSmepAiTests : NpcTestBase
    {
        [TestCase("smep", true)]
        [TestCase("SMEP", true)]
        [TestCase("gof3", false)]
        [TestCase("es2", false)]
        [TestCase(null, false)]
        public void ModRequestTakesPrecedenceIndependentOfGlobalPreset(string requestedAi, bool expected)
        {
            Assert.AreEqual(expected, SmepAiRules.RequestedByMod(requestedAi));
        }

        [TestCase(false, false, true)]
        [TestCase(false, true, true)]
        [TestCase(true, false, false)]
        [TestCase(true, true, true)]
        public void MobilePlatformRequiresAnExplicitModOptIn(bool mobilePlatform, bool mobileOptIn, bool expected)
        {
            Assert.AreEqual(expected, SmepAiRules.EnabledOnPlatform(mobilePlatform, mobileOptIn));
        }

        [TestCase(false, false, true)]
        [TestCase(false, true, true)]
        [TestCase(true, false, false)]
        [TestCase(true, true, false)]
        public void DesktopHostAlsoRequiresOptInForMobileMultiplayerTargets(bool mobileTarget, bool mobileOptIn, bool expected)
        {
            Assert.AreEqual(expected, SmepAiRules.EnabledForTarget(mobileTarget, mobileOptIn));
        }

        [TestCase(true, false, false, true, true)]
        [TestCase(true, false, false, false, false)]
        [TestCase(false, true, true, false, true)]
        [TestCase(false, true, false, true, false)]
        [TestCase(false, false, false, true, false)]
        public void MobileTargetClassificationUsesTheOwningPlayersPlatform(bool isPlayer, bool netPilot,
                                                                            bool remoteMobilePlatform, bool localMobilePlatform,
                                                                            bool expected)
        {
            Assert.AreEqual(expected, SmepAiRules.IsMobileTarget(isPlayer, netPilot, remoteMobilePlatform, localMobilePlatform));
        }

        [TestCase(true, false, false, true, false, true)]
        [TestCase(true, true, false, true, false, false)]
        [TestCase(false, false, false, true, false, false)]
        [TestCase(true, false, true, true, false, false)]
        [TestCase(true, false, false, false, false, false)]
        [TestCase(true, false, false, true, true, false)]
        public void CustomControllerPrecedenceHonoursNpcAndMobileFallback(bool eligibleNpc, bool mobileTarget,
            bool empDisabled, bool canTurn, bool drifting, bool expected)
        {
            Assert.AreEqual(expected, SmepAiRules.UseCustomFlight(eligibleNpc, mobileTarget, empDisabled, canTurn, drifting));
        }

        [TestCase("frigate", true)]
        [TestCase("CRUISER", true)]
        [TestCase("Carrier", true)]
        [TestCase("FREIGHTER", true)]
        [TestCase("interceptor", false)]
        [TestCase("heavy_bomber", false)]
        [TestCase(null, false)]
        public void ExcludedNpcClassesDoNotDependOnCapitalization(string classId, bool excluded)
        {
            Assert.AreEqual(excluded, SmepAiRules.ExcludedNpcClass(classId));
        }

        [Test]
        public void MobileOptInDoesNotOverrideMobileTargetFallback()
        {
            // Mobile AI opt-in controls the build, not the target's controller.
            Assert.IsTrue(SmepAiRules.EnabledOnPlatform(true, true));
            Assert.IsFalse(SmepAiRules.EnabledForTarget(true, true));
            Assert.IsFalse(SmepAiRules.UseCustomFlight(true, true, false, true, false));
        }

        [Test]
        public void MobilePilotJoiningMidFightForcesConventionalFlight()
        {
            Assert.IsTrue(SmepAiRules.UseCustomFlight(true, false, false, true, false));
            Assert.IsFalse(SmepAiRules.UseCustomFlight(true, true, false, true, false));
            Assert.IsTrue(SmepAiRules.UseCustomFlight(true, false, false, true, false));
        }

        [Test]
        public void PredictivePathCacheRetestsOnlyWhenStaleOrTurnedSharply()
        {
            var forward = UnityEngine.Vector3.forward;
            Assert.IsTrue(SmepAiRules.RefreshPath(false, 100f, forward, forward));
            Assert.IsTrue(SmepAiRules.RefreshPath(true, 0f, forward, forward));
            Assert.IsFalse(SmepAiRules.RefreshPath(true, 100f, forward, forward));
            Assert.IsFalse(SmepAiRules.RefreshPath(true, 100f, UnityEngine.Quaternion.Euler(0f, 20f, 0f) * forward, forward));
            Assert.IsTrue(SmepAiRules.RefreshPath(true, 100f, UnityEngine.Quaternion.Euler(0f, 40f, 0f) * forward, forward));
        }

        [Test]
        public void CollisionVelocityUsesWholeFrameMovementNotAddedCorrection()
        {
            var start = new UnityEngine.Vector3(2f, 0f, 0f);
            var afterThrust = new UnityEngine.Vector3(5f, 0f, 0f);
            var afterCollision = new UnityEngine.Vector3(4f, 0f, 0f);
            var velocity = SmepAiRules.VelocityFromDisplacement(start, afterCollision, 100f, 1f, 10f);
            Assert.That(velocity.x, Is.EqualTo(0.02f).Within(0.00001f));
            Assert.That(SmepAiRules.VelocityFromDisplacement(start, afterThrust, 100f, 1f, 10f).x,
                        Is.EqualTo(0.03f).Within(0.00001f));
        }

        [Test]
        public void CollisionCorrectionCannotExceedSharedSpeedLimit()
        {
            var velocity = SmepAiRules.VelocityFromDisplacement(UnityEngine.Vector3.zero,
                             new UnityEngine.Vector3(200f, 0f, 0f), 100f, 1f, 0.5f);
            Assert.That(velocity.magnitude, Is.EqualTo(0.5f).Within(0.00001f));
            Assert.AreEqual(UnityEngine.Vector3.zero, SmepAiRules.VelocityFromDisplacement(
                            UnityEngine.Vector3.zero, UnityEngine.Vector3.one, 0f, 1f, 1f));
        }

        [Test]
        public void ExistingModsDoNotOptIntoMobileSmepByDefault()
        {
            Assert.IsFalse(new GoF2Remake.Data.CustomTraffic().mobileAi);
            Assert.IsFalse(UnityEngine.JsonUtility.FromJson<GoF2Remake.Data.CustomTraffic>("{}").mobileAi);
            Assert.IsFalse(UnityEngine.JsonUtility.FromJson<GoF2Remake.Data.CustomTraffic>("{\"ai\":\"smep\"}").mobileAi);
        }

        [Test]
        public void FallbackFireHonoursNativeNpcFireRestrictions()
        {
            bool Allowed(bool shootingEnabled = true, bool radarHidden = false, bool holdFire = false, bool targetable = true,
                         bool cloaked = false, bool followingWaypoint = false, bool attacking = true, bool roleFires = true,
                         bool smepHoldFire = false, bool es2Suppressed = false, bool dockedPlayerAbove = false)
                => SmepAiRules.CanFireFallback(shootingEnabled, radarHidden, holdFire, targetable, cloaked, followingWaypoint,
                                               attacking, roleFires, smepHoldFire, es2Suppressed, dockedPlayerAbove);

            Assert.IsTrue(Allowed(), "an otherwise eligible target may be fired upon");
            Assert.IsFalse(Allowed(shootingEnabled: false), "level scripts can disable NPC shooting");
            Assert.IsFalse(Allowed(radarHidden: true), "hidden ships do not fire");
            Assert.IsFalse(Allowed(holdFire: true), "warp / jam hold-fire is respected");
            Assert.IsFalse(Allowed(targetable: false), "untargetable targets are not fired upon");
            Assert.IsFalse(Allowed(cloaked: true), "cloaked targets are not fired upon");
            Assert.IsFalse(Allowed(followingWaypoint: true), "route following suppresses fallback fire");
            Assert.IsFalse(Allowed(attacking: false), "non-attacking ships do not fire");
            Assert.IsFalse(Allowed(roleFires: false), "non-firing roles stay unarmed");
            Assert.IsFalse(Allowed(smepHoldFire: true), "the dogfight state can hold fire");
            Assert.IsFalse(Allowed(es2Suppressed: true), "the ES2 crowd director can suppress fire");
            Assert.IsFalse(Allowed(dockedPlayerAbove: true), "a docked player above the NPC is not fired upon");
        }
    }
}
