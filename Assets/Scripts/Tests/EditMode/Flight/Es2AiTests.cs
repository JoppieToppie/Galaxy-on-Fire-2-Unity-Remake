// Es2AiTests.cs
// Feature: docs/notes/npcs-and-combat.md "ES2 aiming" and "ES2 dogfighting" (Flight.Es2Aim, Es2Director, Es2Dogfight, after
// EVERSPACE 2's FBehaviourTreeTaskLib::Attack, DT_AimingNoiseTable, FAiDirectorParameter and BT_Default): the aim's inertia by
// row, target and difficulty, the reticule's easing, the lead, the noise, the crowd's suppression, the dogfight's runs,
// gaining distance, tailed evasion and flight, and the physics flight's turn and thrust rules (Flight.Es2NpcFlight).

using GoF2Remake.Flight;
using NUnit.Framework;

namespace GoF2Remake.Tests
{
    public class Es2AiTests
    {
        [Test]
        public void TheAimSettlesFasterOnHigherDifficulties()
        {
            Assert.AreEqual(1.3f, Es2Aim.DifficultyFactor(0f), 1e-6f, "Easy: ES2's Easy");
            Assert.AreEqual(1f, Es2Aim.DifficultyFactor(0.5f), 1e-6f, "Normal");
            Assert.AreEqual(0.5f, Es2Aim.DifficultyFactor(1f), 1e-6f, "Hard");
            Assert.AreEqual(0.25f, Es2Aim.DifficultyFactor(1.5f), 1e-6f, "Extreme: ES2's VeryHard");
            Assert.AreEqual(0.2f, Es2Aim.InertiaS(Es2Aim.Projectile, true, 0.5f), 1e-6f, "a projectile row against a player on Normal");
            Assert.AreEqual(0.05f, Es2Aim.InertiaS(Es2Aim.Projectile, true, 1.5f), 1e-6f);
            Assert.AreEqual(0.6f, Es2Aim.InertiaS(Es2Aim.Projectile, false, 1.5f), 1e-6f, "against an NPC: no difficulty");
            Assert.AreEqual(0.3f, Es2Aim.InertiaS(Es2Aim.Sniper, true, 1.5f), 1e-6f, "the sniper row ignores the difficulty");
        }

        [Test]
        public void TheMissGrowsWithTheDistance()
        {
            Assert.AreEqual(1000f * (float)System.Math.Tan(0.1 * System.Math.PI / 180.0), Es2Aim.NoiseAmplitude(Es2Aim.Projectile, true, 1000f), 1e-4f);
            Assert.AreEqual(0f, Es2Aim.NoiseAmplitude(Es2Aim.Sniper, true, 1000f), "a sniper doesn't miss");
            for (double t = 0; t < 20; t += 0.37)
            {
                float n = Es2Aim.Noise(t, 1f, 7);
                Assert.That(n, Is.InRange(-0.5f, 0.5f));
            }
            Assert.AreEqual(Es2Aim.Noise(3.2, 1f, 7), Es2Aim.Noise(3.2, 1f, 7), "the same for the same time and seed");
            Assert.Less(System.Math.Abs(Es2Aim.Noise(3.2, 1f, 7) - Es2Aim.Noise(3.21, 1f, 7)), 0.05f, "smooth");
        }

        [Test]
        public void TheReticuleSlidesOntoTheLeadOverTheInertia()
        {
            var aim = new Es2Aim();
            Assert.IsTrue(aim.Due, "a new aim point at once");
            aim.Begin(0.2f);
            Assert.IsFalse(aim.Due);
            Assert.AreEqual(0.25f, aim.Blend(0.05f), 1e-5f, "a quarter of the way in a quarter of the time");
            Assert.AreEqual(1f / 3f, aim.Blend(0.05f), 1e-5f, "a third of what is left");
            Assert.AreEqual(1f, aim.Blend(0.2f), 1e-6f, "there once the time is up");
            Assert.IsTrue(aim.Due);
        }

        [Test]
        public void TheLeadMeetsAMovingTarget()
        {
            // a target 100 ahead crossing at 1 a tick, the shot at 2 a tick
            float t = Es2Aim.InterceptTime(0f, 0f, 100f, 1f, 0f, 0f, 2f);
            float dx = 1f * t, dz = 100f;
            Assert.AreEqual(2f * t, (float)System.Math.Sqrt(dx * dx + dz * dz), 1e-2f, "the shot covers the way to where the target is then");
            Assert.AreEqual(50f, Es2Aim.InterceptTime(0f, 0f, 100f, 0f, 0f, 0f, 2f), 1e-4f, "a still target: the straight time");
            Assert.AreEqual(50f, Es2Aim.InterceptTime(0f, 0f, 100f, 0f, 0f, 5f, 2f), 1e-4f, "one it can't catch: the straight time");
        }

        [Test]
        public void OfACrowdOnlyAFewShootAtOnce()
        {
            Assert.AreEqual(0f, Es2Director.SuppressChance(3), "up to 3: all shoot");
            Assert.AreEqual(6f, Es2Director.Unsuppressed(12), 1e-6f);
            Assert.AreEqual(0.5f, Es2Director.SuppressChance(12), 1e-6f, "a dozen: half the time each, 6 at once");
            Assert.AreEqual(1f - 6f / 40f, Es2Director.SuppressChance(40), 1e-6f, "still about 6 of 40");
            Assert.LessOrEqual(Es2Director.SuppressChance(1000), Es2Director.MaxSuppress, "each shoots a tenth of the time at least");
            for (double t = 0; t < 30; t += 0.5) Assert.IsFalse(Es2Director.HoldsFire(2, t, 11), "a pair never holds");
            int held = 0, n = 0;
            for (int seed = 0; seed < 40; seed++)
                for (double t = 0; t < 80; t += 0.5, n++) if (Es2Director.HoldsFire(12, t, seed)) held++;
            Assert.That(held / (float)n, Is.InRange(0.3f, 0.7f), "about half of the time with 12");
        }

        static Es2Dogfight Make(float rollChance, params float[] rolls)
        {
            int i = 0;   // the rolls in turn, the last one again and again
            return new Es2Dogfight(rollChance, () => rolls[System.Math.Min(i++, rolls.Length - 1)]);
        }

        static Es2Dogfight.View Near => new Es2Dogfight.View { distance = 10000f, hullRatio = 1f };

        [Test]
        public void ARunEndsInGainingDistanceThenTheNextRun()
        {
            var d = Make(0.3f, 0.5f);
            Assert.AreEqual(Es2Dogfight.Mode.Attack, d.Current);
            Assert.That(d.OffsetUnits, Is.InRange(Es2Dogfight.OffsetMinM * Es2Dogfight.UnitsPerMetre, Es2Dogfight.OffsetMaxM * Es2Dogfight.UnitsPerMetre));
            d.Step(16f, Near);
            Assert.AreEqual(Es2Dogfight.Mode.Attack, d.Current, "not there yet");
            d.Step(16f, new Es2Dogfight.View { distance = 10000f, arrived = true, hullRatio = 1f });
            Assert.AreEqual(Es2Dogfight.Mode.FlyAway, d.Current, "0.5: no orbit, away");
            Assert.IsTrue(d.Fires, "ES2 shoots while flying away when it can");
            d.Step(16f, new Es2Dogfight.View { distance = Es2Dogfight.FlyAwayM * Es2Dogfight.UnitsPerMetre, hullRatio = 1f });
            Assert.AreEqual(Es2Dogfight.Mode.Attack, d.Current, "400 m away: the next run");
        }

        [Test]
        public void AFifthOfTheTimeItOrbitsFirst()
        {
            var d = Make(0.3f, 0.5f, 0.5f, 0.5f, 0.1f);   // (angle, offset, the gain window, then the orbit roll)
            d.Step(16f, new Es2Dogfight.View { distance = 10000f, arrived = true, hullRatio = 1f });
            Assert.AreEqual(Es2Dogfight.Mode.Orbit, d.Current);
            d.Step(Es2Dogfight.OrbitMs * (1f + Es2Dogfight.OrbitVariance), Near);
            Assert.AreEqual(Es2Dogfight.Mode.FlyAway, d.Current, "the orbit's time up: away");
        }

        [Test]
        public void TailedItZigzagsRollsOrTurnsAndFights()
        {
            var tailed = new Es2Dogfight.View { distance = 10000f, tailed = true, hullRatio = 1f };
            var zig = Make(0.3f, 0.5f, 0.5f, 0.5f, 0.5f, 0.5f, 0.1f);   // ... no orbit, the tail check's interval, then 0.1 < 0.65
            zig.Step(16f, new Es2Dogfight.View { distance = 10000f, arrived = true, hullRatio = 1f });
            zig.Step(Es2Dogfight.TailCheckMs + Es2Dogfight.TailCheckDeviationMs, tailed);
            Assert.AreEqual(Es2Dogfight.Mode.Zigzag, zig.Current);

            var roll = Make(0.3f, 0.5f, 0.5f, 0.5f, 0.5f, 0.5f, 0.5f, 0.9f, 0.1f);   // 0.9: no zig-zag; 0.1 < 0.3: roll
            roll.Step(16f, new Es2Dogfight.View { distance = 10000f, arrived = true, hullRatio = 1f });
            roll.Step(Es2Dogfight.TailCheckMs + Es2Dogfight.TailCheckDeviationMs, tailed);
            Assert.AreEqual(Es2Dogfight.Mode.BarrelRoll, roll.Current);
            Assert.IsFalse(roll.Fires, "no shots in a roll");
            roll.Step(Es2Dogfight.RollMs, Near);
            Assert.AreEqual(Es2Dogfight.Mode.Zigzag, roll.Current, "then zig-zag away");

            var easy = Make(Es2Dogfight.RollChanceFor(0f), 0.5f, 0.5f, 0.5f, 0.5f, 0.5f, 0.5f, 0.9f, 0.1f);
            easy.Step(16f, new Es2Dogfight.View { distance = 10000f, arrived = true, hullRatio = 1f });
            easy.Step(Es2Dogfight.TailCheckMs + Es2Dogfight.TailCheckDeviationMs, tailed);
            Assert.AreEqual(Es2Dogfight.Mode.TurnAndFight, easy.Current, "Easy: no barrel roll");
        }

        [Test]
        public void ItFleesOnceBelowAFifthOfItsHull()
        {
            var d = Make(0.3f, 0.5f);
            d.Step(16f, new Es2Dogfight.View { distance = 10000f, hullRatio = 0.15f });
            Assert.AreEqual(Es2Dogfight.Mode.Flee, d.Current);
            d.Step(16f, new Es2Dogfight.View { distance = Es2Dogfight.FleeSafeM * Es2Dogfight.UnitsPerMetre, hullRatio = 0.15f });
            Assert.AreEqual(Es2Dogfight.Mode.FleeIdle, d.Current, "far enough: it loiters");
            d.Step(Es2Dogfight.FleeMaxMs, new Es2Dogfight.View { distance = 90000f, hullRatio = 0.15f });
            Assert.AreEqual(Es2Dogfight.Mode.Attack, d.Current, "no shield to recharge: back after a while");
            d.Step(16f, new Es2Dogfight.View { distance = 10000f, hullRatio = 0.1f });
            Assert.AreEqual(Es2Dogfight.Mode.Attack, d.Current, "once");
        }

        [Test]
        public void TailedMeansBehindUsNoseOnUsAndCloseToItsLine()
        {
            // we fly +z; the target 100 behind us flying +z (at us), 10 off our axis; our radius 5 -> its line within 15
            Assert.IsTrue(Es2Dogfight.IsTailed(10f, 0f, -100f, 0f, 0f, 1f, 0f, 0f, 1f, 5f));
            Assert.IsFalse(Es2Dogfight.IsTailed(20f, 0f, -100f, 0f, 0f, 1f, 0f, 0f, 1f, 5f), "too far off its line");
            Assert.IsFalse(Es2Dogfight.IsTailed(10f, 0f, -100f, 0f, 0f, 1f, 0f, 0f, -1f, 5f), "its nose away from us");
            Assert.IsFalse(Es2Dogfight.IsTailed(10f, 0f, 100f, 0f, 0f, 1f, 0f, 0f, -1f, 5f), "in front of us");
        }
    
        [Test]
        public void ATurnCoastsOutWithoutOvershooting()
        {
            float err = 90f, rate = 0f, dt = 1f / 60f, over = 0f;
            float top = Es2NpcFlight.TopTurnDegS, acc = Es2NpcFlight.TurnAccelDegS2;
            Assert.AreEqual(42f, top, 0.1f, "GoF2's 48 / 65536 a ms");
            for (int i = 0; i < 600; i++)
            {
                rate = Es2NpcFlight.Ramp(rate, Es2NpcFlight.RotationInput(err, rate, acc) * top, acc, dt);
                err -= rate * dt;
                if (err < 0f) over = System.Math.Max(over, -err);
            }
            Assert.AreEqual(0f, err, 0.5f, "on the heading");
            Assert.Less(over, 3f, "eased off before it");
            Assert.AreEqual(0f, Es2NpcFlight.RotationInput(4f, 40f, acc), "inside the braking angle: it coasts");
            Assert.AreEqual(0.8f, Es2NpcFlight.RotationInput(4f, -40f, acc), 1e-5f, "turning away: no braking angle (4 of the 5 deg ease)");
        }

        [Test]
        public void TheThrustEasesAndStopsInTime()
        {
            Assert.AreEqual(1f, Es2NpcFlight.Ease(0f, 2f));
            Assert.AreEqual(0f, Es2NpcFlight.Ease(2f, 2f));
            Assert.AreEqual(0f, Es2NpcFlight.LinearInput(10f, 20f, 5f), "inside the stopping distance: brake");
            Assert.AreEqual(1f, Es2NpcFlight.LinearInput(100f, 20f, 5f));
            Assert.AreEqual(0.5f, Es2NpcFlight.LinearInput(22.5f, 20f, 5f), 1e-6f, "eased between");
            Assert.AreEqual(25f, Es2NpcFlight.StoppingDistance(10f, 2f), 1e-5f);
            Assert.AreEqual(13, Es2NpcFlight.Candidates.Length, "ES2's exterior table");
            Assert.AreEqual((0f, 1f), Es2NpcFlight.Candidates[0], "straight on first");
        }
    }
}
