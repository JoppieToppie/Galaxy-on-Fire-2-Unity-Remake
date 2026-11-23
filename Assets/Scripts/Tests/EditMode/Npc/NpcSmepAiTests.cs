// NpcSmepAiTests.cs
// Regression coverage for the mod opt-in boundary and SMEP's fallback firing safeguards.

using NUnit.Framework;
using GoF2Remake.World;

namespace GoF2Remake.Tests
{
    public class NpcSmepAiTests : NpcTestBase
    {
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
        [TestCase(true, true, true)]
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
