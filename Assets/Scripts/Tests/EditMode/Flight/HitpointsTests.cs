// HitpointsTests.cs
// The damage pools (CLAUDE.md "NPCs and combat", Damage: shield -> armor -> hull, no reduction; Reference/research/
// ship_combat.md 2.1 Player::damage, 2.2 shield regen / repair bots, 2.4 EMP). Expected numbers by hand from those
// listings: the shield's fraction truncated on every hit, the overflow off the armor then the hull, hull < 1 = dead;
// regen + maxShield / (attr19 / 100) per 100 ms remake tick (Targe 50 / 20000 -> 0.25), repair bot hull +1 per 600 /
// armor +2 per 1000 once the hull is full; EMP recovery int(timer / recovery * maxEmp), back when > maxEmp.

using GoF2Remake.Flight;
using NUnit.Framework;

namespace GoF2Remake.Tests
{
    public class HitpointsTests
    {
        [TestCase(100)]
        [TestCase(1000)]
        public void AppliedHullDamageReportsOnlyActualHullFraction(int maxHull)
        {
            var go = new UnityEngine.GameObject("fleet damage target");
            try
            {
                var target = go.AddComponent<Target>();
                target.customDeath = true;
                target.hitpoints = new Hitpoints(maxHull, 40, 20);
                double playerDamage = 0d, npcDamage = 0d;
                target.HullDamageApplied += (_, fraction, byNpc) =>
                {
                    if (byNpc) npcDamage += fraction;
                    else playerDamage += fraction;
                };
                target.Damage(40);
                target.Damage(20);
                Assert.AreEqual(0d, playerDamage, 1e-9d, "shields and armor are not hull");
                target.Damage(maxHull / 4);
                Assert.AreEqual(0.25d, playerDamage, 1e-9d);
                target.Damage(maxHull / 4, true);
                Assert.AreEqual(0.25d, npcDamage, 1e-9d);
                target.Damage(maxHull * 10);
                Assert.AreEqual(0.75d, playerDamage, 1e-9d, "only remaining hull counts, not overkill");
                target.Damage(maxHull);
                Assert.AreEqual(0.75d, playerDamage, 1e-9d, "dead targets cannot count twice");
            }
            finally
            {
                UnityEngine.Object.DestroyImmediate(go);
            }
        }

        [Test]
        public void NewPoolsAreFull()
        {
            var h = new Hitpoints(200, 50, 40);
            Assert.AreEqual(200, h.hull); Assert.AreEqual(200, h.maxHull);
            Assert.AreEqual(50f, h.shield); Assert.AreEqual(50, h.maxShield);
            Assert.AreEqual(40, h.armor); Assert.AreEqual(40, h.maxArmor);
            Assert.IsTrue(h.Alive);
            Assert.AreEqual(290f, h.Combined);
            Assert.AreEqual(1f, h.HullFraction);
        }

        [Test]
        public void ShieldTakesTheHitFirst()
        {
            var h = new Hitpoints(200, 50, 40);
            Assert.IsFalse(h.Damage(30));
            Assert.AreEqual(20f, h.shield);
            Assert.AreEqual(40, h.armor);
            Assert.AreEqual(200, h.hull);
            Assert.IsTrue(h.shieldHit);
            Assert.IsFalse(h.armorHit || h.hullHit);
        }

        [Test]
        public void AShieldFactorScalesOnlyTheShieldsPart()
        {
            // remake (Flight.Gun.ShieldFactor, the Disruptor's x2): 10 at x2 takes 20 off the shield
            var h = new Hitpoints(200, 50, 40);
            h.Damage(10, 2f);
            Assert.AreEqual(30f, h.shield);
            Assert.AreEqual(40, h.armor);
            // 30 at x2 = 60 on a shield of 30: the shield stops 15 of it, the other 15 go on at their plain value
            h.Damage(30, 2f);
            Assert.AreEqual(0f, h.shield);
            Assert.AreEqual(25, h.armor);
            Assert.AreEqual(200, h.hull);
            // no shield left: plain damage
            h.Damage(30, 2f);
            Assert.AreEqual(0, h.armor);
            Assert.AreEqual(195, h.hull);
            // below 1: a weak weapon against shields
            var w = new Hitpoints(200, 50, 0);
            w.Damage(20, 0.5f);
            Assert.AreEqual(40f, w.shield);
        }

        [Test]
        public void ShieldFractionIsTruncatedOnEveryHit()
        {
            var h = new Hitpoints(200, 50);
            h.shield = 10.75f;
            h.Damage(3);
            Assert.AreEqual(7f, h.shield);   // int(10.75) - 3
        }

        [Test]
        public void OverflowGoesToArmorThenHull()
        {
            var h = new Hitpoints(100, 5, 10);
            h.Damage(12);                    // s = -7, armor 10 - 7
            Assert.AreEqual(0f, h.shield);
            Assert.AreEqual(3, h.armor);
            Assert.AreEqual(100, h.hull);
            Assert.IsTrue(h.armorHit);
            h.Damage(20);                    // s = -20, a = -17
            Assert.AreEqual(0, h.armor);
            Assert.AreEqual(83, h.hull);
            Assert.IsTrue(h.hullHit);
        }

        [Test]
        public void ExactlyEmptyShieldStaysAShieldHit()
        {
            var h = new Hitpoints(100, 10, 10);
            h.Damage(10);
            Assert.AreEqual(0f, h.shield);
            Assert.AreEqual(10, h.armor);
            Assert.IsTrue(h.shieldHit);
            Assert.IsFalse(h.armorHit);
        }

        [Test]
        public void HullBelowOneKills()
        {
            var h = new Hitpoints(10);
            Assert.IsFalse(h.Damage(9));
            Assert.AreEqual(1, h.hull);
            Assert.IsTrue(h.Damage(5));
            Assert.AreEqual(0, h.hull, "clamped at 0");
            Assert.IsFalse(h.Alive);
            Assert.IsFalse(h.Damage(5), "a dead ship isn't killed again");
        }

        [Test]
        public void InvulnerableOrZeroDamageDoesNothing()
        {
            var h = new Hitpoints(100, 20) { vulnerable = false };
            Assert.IsFalse(h.Damage(1000));
            Assert.AreEqual(100, h.hull);
            Assert.AreEqual(20f, h.shield);
            h.vulnerable = true;
            Assert.IsFalse(h.Damage(0));
            Assert.IsFalse(h.Damage(-5));
            Assert.AreEqual(20f, h.shield);
        }

        [Test]
        public void NpcsHaveOnlyHull()
        {
            var h = new Hitpoints(50);
            h.Damage(20);
            Assert.AreEqual(30, h.hull);
            Assert.AreEqual(0.6f, h.HullFraction, 1e-6f);
            Assert.AreEqual(0f, h.ShieldFraction);
            Assert.AreEqual(0f, h.ArmorFraction);
        }

        // ---- shield regen, repair bots ------------------------------------------------------------------------------

        [TestCase(DamageType.None)]
        [TestCase(DamageType.Energy)]
        [TestCase(DamageType.Ballistic)]
        public void ShieldRegenWaitsAfterDamage(DamageType type)
        {
            var h = new Hitpoints(100, 50) { shieldRechargeDelayMs = 5000 };
            h.Damage(10, 1f, type);
            float charge = h.shield;
            h.RegenerateShield(4999f, 20000);
            Assert.AreEqual(charge, h.shield);
            h.RegenerateShield(1f, 20000);
            Assert.AreEqual(charge, h.shield);
            h.RegenerateShield(101f, 20000);
            Assert.Greater(h.shield, charge);
        }

        [Test]
        public void HullDamageResetsDelayEvenWithEmptyShield()
        {
            var h = new Hitpoints(100, 10) { shieldRechargeDelayMs = 5000 };
            h.Damage(20);
            h.RegenerateShield(4000f, 20000);
            h.Damage(1);
            h.RegenerateShield(4999f, 20000);
            Assert.AreEqual(0f, h.shield);
            Assert.AreEqual(1f, h.ShieldRechargeRemainingMs);
            h.RegenerateShield(102f, 20000);
            Assert.Greater(h.shield, 0f);
        }

        [Test]
        public void RechargeCooldownUsesElapsedTimeAndCanBeRestored()
        {
            var h = new Hitpoints(100, 50) { shieldRechargeDelayMs = 5000, shield = 10 };
            h.RestoreShieldRechargeDelay(3000f);
            h.RegenerateShield(0f, 20000, 3000f);
            Assert.AreEqual(0f, h.ShieldRechargeRemainingMs);
            Assert.AreEqual(10f, h.shield);
            h.RegenerateShield(101f, 20000);
            Assert.Greater(h.shield, 10f);
        }

        [Test]
        public void ShieldRegenKeepsRemainderAndCatchesUp()
        {
            var h = new Hitpoints(100, 50) { shield = 0f };
            h.RegenerateShield(99f, 20000);
            Assert.AreEqual(0f, h.shield);
            h.RegenerateShield(326f, 20000);
            Assert.AreEqual(1f, h.shield);
            h.RegenerateShield(75f, 20000);
            Assert.AreEqual(1.25f, h.shield);
        }

        [Test]
        public void ShieldRegenStopsAtMax()
        {
            var h = new Hitpoints(100, 150);   // Beamshield II: 150 / 45000 -> 0.333 per tick
            h.shield = 149.9f;
            h.RegenerateShield(100f, 45000);
            Assert.AreEqual(150f, h.shield);
        }

        [Test]
        public void ShieldRegenFullInAttr19Ticks()
        {
            var h = new Hitpoints(100, 50);
            h.shield = 0f;
            for (int i = 0; i < 199; i++) h.RegenerateShield(100f, 20000);
            Assert.Less(h.shield, 50f);
            h.RegenerateShield(100f, 20000);
            Assert.AreEqual(50f, h.shield, 1e-3f);
        }

        [Test]
        public void NoRegenWhenDeadOrWithoutTime()
        {
            var dead = new Hitpoints(10, 50);
            dead.shield = 0f;
            dead.hull = 0;
            dead.RegenerateShield(500f, 20000);
            Assert.AreEqual(0f, dead.shield);
            var none = new Hitpoints(10, 50);
            none.shield = 0f;
            none.RegenerateShield(500f, 0);
            Assert.AreEqual(0f, none.shield);
        }

        [Test]
        public void RepairBotHullFirstThenArmor()
        {
            // Ketar Repair Bot: hull +1 / 600 ms, armor +2 / 1000 ms only with a full hull
            var h = new Hitpoints(100, 0, 20);
            h.hull = 98;
            h.armor = 10;
            h.Repair(600f, 600f, 1000f);
            Assert.AreEqual(99, h.hull);
            h.Repair(400f, 600f, 1000f);   // armor timer reaches 1000, hull not full: no armor
            Assert.AreEqual(10, h.armor);
            h.Repair(200f, 600f, 1000f);   // hull timer 600
            Assert.AreEqual(100, h.hull);
            h.Repair(1000f, 600f, 1000f);
            Assert.AreEqual(12, h.armor);
        }

        [TestCase(600, 1000)]
        [TestCase(420, 700)]
        public void RegenerationIsIndependentOfFramePartition(int hullMs, int armorMs)
        {
            // The fifth hull tick coincides with the third armor tick for both bots.
            int elapsedMs = 5 * hullMs + 1;
            foreach (int fps in new[] { 1, 30, 60, 100, 144 })
            {
                var h = new Hitpoints(100, 50, 20) { hull = 95, armor = 0, shield = 0f };
                int previous = 0;
                for (int frame = 1; previous < elapsedMs; frame++)
                {
                    int now = System.Math.Min(frame * 1000 / fps, elapsedMs);
                    h.RegenerateShield(now - previous, 20000);
                    h.Repair(now - previous, hullMs, armorMs);
                    previous = now;
                }
                Assert.AreEqual(elapsedMs / 100 * 0.25f, h.shield);
                Assert.AreEqual(100, h.hull);
                Assert.AreEqual(2, h.armor);
            }

            var hitch = new Hitpoints(100, 0, 20) { hull = 95, armor = 0 };
            hitch.Repair(elapsedMs, hullMs, armorMs);
            Assert.AreEqual(100, hitch.hull);
            Assert.AreEqual(2, hitch.armor);
        }

        [Test]
        public void RepairBotArmorStopsAtMax()
        {
            var h = new Hitpoints(100, 0, 20);
            h.armor = 19;
            h.Repair(700f, 420f, 700f);   // Ketar II
            Assert.AreEqual(20, h.armor);
        }

        [Test]
        public void NoRepairWhenDead()
        {
            var h = new Hitpoints(100);
            h.hull = 0;
            h.Repair(5000f, 600f, 1000f);
            Assert.AreEqual(0, h.hull);
        }

        // ---- EMP ---------------------------------------------------------------------------------------------------

        [Test]
        public void EmpDisablesAtZero()
        {
            // NPC: 5 * min(level, 20) + 40 points, 15000 ms recovery
            var h = new Hitpoints(100);
            h.SetEmp(40, 15000f);
            Assert.IsFalse(h.DamageEmp(39));
            Assert.AreEqual(1, h.emp);
            Assert.IsFalse(h.empDisabled);
            Assert.IsTrue(h.DamageEmp(5));
            Assert.AreEqual(0, h.emp);
            Assert.IsTrue(h.empDisabled);
            Assert.IsFalse(h.DamageEmp(5), "nothing left to drain");
        }

        [Test]
        public void EmpRecoversLinearly()
        {
            var h = new Hitpoints(100);
            h.SetEmp(40, 15000f);
            h.DamageEmp(40);
            h.Update(7500f);
            Assert.AreEqual(20, h.emp);
            Assert.IsTrue(h.empDisabled);
            h.Update(7500f);
            Assert.AreEqual(40, h.emp);
            Assert.IsTrue(h.empDisabled, "back only when past the maximum");
            h.Update(1f);                    // int(15001 / 15000 * 40) = 40: not past it yet
            Assert.IsTrue(h.empDisabled);
            h.Update(400f);                  // int(41.07) = 41 > 40
            Assert.AreEqual(40, h.emp);
            Assert.IsFalse(h.empDisabled);
        }

        [Test]
        public void NoEmpPoolMeansImmune()
        {
            var h = new Hitpoints(100);   // the player: no EMP points
            Assert.IsFalse(h.DamageEmp(1000));
            Assert.IsFalse(h.empDisabled);
            Assert.AreEqual(1f, h.EmpFraction);
        }

        [Test]
        public void EmpIgnoredWhenInvulnerableOrDead()
        {
            var h = new Hitpoints(100) { vulnerable = false };
            h.SetEmp(40, 15000f);
            Assert.IsFalse(h.DamageEmp(100));
            Assert.AreEqual(40, h.emp);
            h.vulnerable = true;
            h.hull = 0;
            Assert.IsFalse(h.DamageEmp(100));
            Assert.AreEqual(40, h.emp);
        }

        [Test]
        public void AFactorOfZeroGoesPastTheShield()
        {
            // GoF3's AbilityBypassShield (a mod weapon's shieldBypass: Gun.ShieldFactor 0)
            var h = new Hitpoints(100, 50, 30);
            Assert.IsFalse(h.Damage(40, 0f));
            Assert.AreEqual(50f, h.shield, "the shield untouched");
            Assert.AreEqual(0, h.armor);
            Assert.AreEqual(90, h.hull, "the armor's 30, then 10 off the hull");
            Assert.IsTrue(h.Damage(200, 0f));
            Assert.AreEqual(0, h.hull);
            Assert.AreEqual(50f, h.shield);
        }

        [Test]
        public void EnergyResistanceReducesShieldDamageAndCarriesOverflow()
        {
            var h = new Hitpoints(100, 100) { energyResistance = 0.5f };
            Assert.IsFalse(h.Damage(100, 1f, DamageType.Energy));
            Assert.AreEqual(50f, h.shield);
            Assert.AreEqual(100, h.hull);

            var immune = new Hitpoints(100, 100) { energyResistance = 1f };
            Assert.IsFalse(immune.Damage(100, 1f, DamageType.Energy));
            Assert.AreEqual(100f, immune.shield, "full resistance absorbs the hit without spending shield points");
            Assert.AreEqual(100, immune.hull);

            var overflow = new Hitpoints(100, 25) { energyResistance = 0.5f };
            Assert.IsFalse(overflow.Damage(100, 1f, DamageType.Energy));
            Assert.AreEqual(0f, overflow.shield);
            Assert.AreEqual(50, overflow.hull, "only the damage beyond the shield's effective capacity reaches hull");
        }

        [Test]
        public void BallisticDamagePenetratesInProportionToRemainingShield()
        {
            var unresisted = new Hitpoints(100, 100);
            Assert.IsFalse(unresisted.Damage(30, 1f, DamageType.Ballistic));
            Assert.AreEqual(70f, unresisted.shield);
            Assert.AreEqual(70, unresisted.hull, "zero resistance means full ballistic penetration");

            var full = new Hitpoints(100, 100) { ballisticResistance = 0.5f };
            Assert.IsFalse(full.Damage(100, 1f, DamageType.Ballistic));
            Assert.AreEqual(0f, full.shield);
            Assert.AreEqual(50, full.hull);

            var half = new Hitpoints(100, 100) { ballisticResistance = 0.5f };
            half.shield = 50f;
            Assert.IsFalse(half.Damage(100, 1f, DamageType.Ballistic));
            Assert.AreEqual(0f, half.shield);
            Assert.AreEqual(25, half.hull, "P = resistance * shield fraction = 0.25");
        }

        [Test]
        public void TypedDamageWithoutResistanceKeepsLegacyPoolBehaviour()
        {
            var h = new Hitpoints(100, 50, 20);
            Assert.IsFalse(h.Damage(30, 1f, DamageType.Energy));
            Assert.AreEqual(20f, h.shield);
            Assert.AreEqual(20, h.armor);
            Assert.AreEqual(100, h.hull);
        }
    }
}
