// ChorusDogfightTests.cs
// Feature: Modding/README.md traffic "ai": "chorus" (Flight.ChorusDogfight, after Chorus's BT_Fighter_CombatRoam): expected,
// an approach at twice the speed firing within 600 m (Chorus's) until 50 m or 10 s, a 4 s pass, 2 s before the next
// approach; a 2 s break when the target aims at it (every 3 s at most); a 5 s retreat below half hull (every 15 s); a jump
// back when out of view for 2 s beyond 1.2 km (every 15 s).

using GoF2Remake.Flight;
using NUnit.Framework;

namespace GoF2Remake.Tests
{
    public class ChorusDogfightTests
    {
        static ChorusDogfight.View At(float d, bool aimed = false, float hull = 1f, bool hidden = false) =>
            new ChorusDogfight.View { distance = d, aimedAt = aimed, hullRatio = hull, outOfView = hidden };

        [Test]
        public void ItApproachesPassesAndComesBack()
        {
            var f = new ChorusDogfight(() => 0.7f);
            Assert.AreEqual(ChorusDogfight.Mode.Approach, f.Current);
            Assert.AreEqual(2f, f.SpeedFactor, "twice the speed on an approach");
            Assert.IsFalse(f.Fires(50000f), "not beyond 600 m (43 200 units)");
            Assert.IsTrue(f.Fires(40000f));
            f.Step(100f, At(3000f));
            Assert.AreEqual(ChorusDogfight.Mode.PassBy, f.Current, "within 50 m: on past it");
            Assert.AreEqual(1, f.Side);
            Assert.IsFalse(f.Fires(1000f));
            f.Step(4000f, At(20000f));
            Assert.AreEqual(ChorusDogfight.Mode.Approach, f.Current);
            Assert.AreEqual(1f, f.SpeedFactor, "the 2 s wait before the next run");
            Assert.IsFalse(f.Fires(20000f));
            f.Step(2100f, At(20000f));
            Assert.IsTrue(f.Fires(20000f));
            f.Step(10000f, At(20000f));
            Assert.AreEqual(ChorusDogfight.Mode.PassBy, f.Current, "an approach lasts 10 s at most");
        }

        [Test]
        public void ItBreaksRetreatsAndJumps()
        {
            var f = new ChorusDogfight(() => 0.2f);
            f.Step(100f, At(30000f, aimed: true));
            Assert.AreEqual(ChorusDogfight.Mode.Evade, f.Current);
            Assert.AreEqual(-1, f.Side);
            f.Step(2000f, At(30000f));
            Assert.AreEqual(ChorusDogfight.Mode.Approach, f.Current, "a 2 s break");
            f.Step(100f, At(30000f, aimed: true));
            Assert.AreEqual(ChorusDogfight.Mode.Approach, f.Current, "not again within 3 s");
            f.Step(100f, At(30000f, hull: 0.4f));
            Assert.AreEqual(ChorusDogfight.Mode.Retreat, f.Current, "below half its hull");
            f.Step(5000f, At(30000f, hull: 0.4f));
            Assert.AreEqual(ChorusDogfight.Mode.Approach, f.Current, "back after 5 s, the next retreat 15 s off");
            Assert.IsFalse(f.Step(1500f, At(90000f, hidden: true)));
            Assert.IsTrue(f.Step(600f, At(90000f, hidden: true)), "2 s out of view beyond 1.2 km: it jumps");
            Assert.IsFalse(f.Step(3000f, At(90000f, hidden: true)), "not again for 15 s");
            Assert.IsFalse(new ChorusDogfight(() => 0.5f).Step(3000f, At(50000f, hidden: true)), "not within 1.2 km");
        }

        [Test]
        public void AScoutBreaksFasterWhenYourNoseIsOnIt()
        {
            Assert.AreEqual(ChorusDogfight.Kind.Scout, ChorusDogfight.KindOf("chorusScout"));
            Assert.AreEqual(ChorusDogfight.Kind.Fighter, ChorusDogfight.KindOf("Chorus"));
            Assert.IsNull(ChorusDogfight.KindOf("gof3"));
            var s = new ChorusDogfight(() => 0.7f, ChorusDogfight.Kind.Scout);
            Assert.AreEqual(25f, s.AimCone);
            Assert.AreEqual(36000f, s.AimRange, 1e-3f, "500 m");
            s.Step(100f, At(30000f, aimed: true));
            Assert.AreEqual(ChorusDogfight.Mode.Evade, s.Current);
            Assert.AreEqual(1.5f, s.SpeedFactor, "the Very Challenging Flee");
            s.Step(2000f, At(30000f));
            s.Step(2100f, At(30000f, aimed: true));
            Assert.AreEqual(ChorusDogfight.Mode.Evade, s.Current, "again after its 4 s");
            var f = new ChorusDogfight(() => 0.7f);
            f.Step(100f, At(30000f, aimed: true));
            Assert.AreEqual(ChorusDogfight.Mode.Evade, f.Current);
            Assert.AreEqual(1f, f.SpeedFactor, "a fighter's break at its own speed");
            Assert.IsFalse(new ChorusDogfight(() => 0.7f).Step(100f, At(40000f, aimed: false)), "(nothing)");
        }

        [Test]
        public void AGunshipHoldsAsATurret()
        {
            var g = new ChorusDogfight(() => 0.5f, ChorusDogfight.Kind.Gunship);
            Assert.AreEqual(ChorusDogfight.Mode.Approach, g.Current);
            Assert.AreEqual(2f, g.SpeedFactor, "in at x2");
            Assert.IsFalse(g.Fires(50000f), "not on its way in");
            g.Step(100f, At(50000f, aimed: true, hull: 0.2f));
            Assert.AreEqual(ChorusDogfight.Mode.Approach, g.Current, "no break, no run away");
            g.Step(100f, At(43000f));
            Assert.AreEqual(ChorusDogfight.Mode.Hold, g.Current, "600 m off");
            Assert.IsTrue(g.Fires(43000f));
            Assert.Less(g.SpeedFactor, 0.5f, "all but still");
            g.Step(100f, At(55000f));
            Assert.AreEqual(ChorusDogfight.Mode.Hold, g.Current, "within 800 m it stays");
            Assert.IsFalse(g.Step(5000f, At(90000f, hidden: true)), "it never jumps");
            Assert.AreEqual(ChorusDogfight.Mode.Approach, g.Current, "past 800 m: in again");
        }

        [Test]
        public void TheOmenKeepAtRange()
        {
            Assert.AreEqual(ChorusDogfight.Kind.Drone, ChorusDogfight.KindOf("chorusDrone"));
            Assert.AreEqual(ChorusDogfight.Kind.Swarm, ChorusDogfight.KindOf("chorusSwarm"));
            var d = new ChorusDogfight(() => 0.5f, ChorusDogfight.Kind.Drone);
            Assert.AreEqual(2f, d.SpeedFactor, "in at x2");
            Assert.IsFalse(d.Fires(30000f), "not on its way in");
            d.Step(100f, At(14000f));
            Assert.AreEqual(ChorusDogfight.Mode.Hold, d.Current, "200 m off: its attack position");
            Assert.IsTrue(d.Fires(14000f));
            Assert.AreEqual(0.75f, d.SpeedFactor);
            d.Step(100f, At(7000f));
            Assert.AreEqual(ChorusDogfight.Mode.PassBy, d.Current, "within 100 m: away");
            Assert.AreEqual(2f, d.SpeedFactor);
            d.Step(3000f, At(30000f));
            Assert.AreEqual(ChorusDogfight.Mode.Approach, d.Current, "in again after 3 s");
            Assert.IsTrue(d.Step(2100f, At(90000f, hidden: true)), "the drone jumps as the fighters");

            var s = new ChorusDogfight(() => 0.5f, ChorusDogfight.Kind.Swarm);
            Assert.AreEqual(1.5f, s.SpeedFactor);
            s.Step(100f, At(28000f));
            Assert.AreEqual(ChorusDogfight.Mode.Hold, s.Current, "400 m off");
            s.Step(100f, At(28000f, hull: 0.4f));
            Assert.AreEqual(ChorusDogfight.Mode.Retreat, s.Current, "below half its hull");
            Assert.IsFalse(s.Step(2100f, At(90000f, hidden: true)), "the swarm never jumps");
        }

        [Test]
        public void TheFleePointIsScoredAwayAndAhead()
        {
            // straight away from the target, along its heading: the best; back at the target or a U-turn: the worst
            float ahead = ChorusDogfight.FleeScore(180f, 0f, 0f, 0f);
            Assert.AreEqual(15f, ahead, 1e-4f, "5 away + 10 the turn");
            Assert.Less(ChorusDogfight.FleeScore(0f, 0f, 0f, 0f), ahead, "at the target scores no exclusion");
            Assert.AreEqual(0f, ChorusDogfight.FleeScore(180f, 180f, 0f, 0f) - 5f, 1e-4f, "a U-turn loses the turn's 10");
            Assert.AreEqual(10f, ChorusDogfight.FleeScore(50f, 30f, 0f, 60f), 1e-4f, "within the run away's 60 deg: excluded");
            Assert.AreEqual(2f, ChorusDogfight.FleeScore(0f, 200f, 1f, 0f), 1e-4f, "the random share");
        }

        [Test]
        public void TrafficPrimariesKeepTheirDamageASecond()
        {
            // Chorus's BP_NPC_Weapon_Main_Scout: 35 every 0.2 s against the fighter's 70 every 0.25 s (0.625 of its damage a
            // second), fired every 200 ms where the game's NPC gun fires every 400: half the shot, at twice the rate
            var t = UnityEngine.JsonUtility.FromJson<GoF2Remake.Data.CustomTraffic>("{ \"primaries\": [ { \"item\": \"chorus:chorus_gatling_t1\", " +
                "\"factor\": 0.625, \"reloadMs\": 200, \"minRank\": 2 } ] }");
            Assert.AreEqual(1, t.primaries.Length);
            Assert.AreEqual(200f, t.primaries[0].reloadMs);
            Assert.IsFalse(t.primaries[0].Carried(1, 0f), "its rank bound, as a secondary's");
            Assert.AreEqual(0.3125f, GoF2Remake.World.NpcShip.PrimaryShotFactor(t.primaries[0], 400f), 1e-5f);
            Assert.AreEqual(0.625f, GoF2Remake.World.NpcShip.PrimaryShotFactor(new GoF2Remake.Data.CustomPrimary { factor = 0.625f }, 400f), 1e-5f,
                "no interval of its own: the game's, the shot scaled as the factor");
        }
    }
}
