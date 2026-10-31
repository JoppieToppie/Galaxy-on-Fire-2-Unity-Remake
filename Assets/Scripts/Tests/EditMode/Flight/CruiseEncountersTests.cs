// CruiseEncountersTests.cs
// The cruise drive's encounter rules (Core/CruiseEncounters.cs; docs/notes/navigation.md "Cruise drive"): the weighted
// pick, an entry's own conditions (rank, security, free play), the roll every 5 s of cruising at 10 % after the first 6 s,
// the 90 s cooldown, at most 2 per orbit, the cruise clock reset when the drive stops. Expected numbers from the class's
// constants and its built-in table (pirates 30, trader 20, derelict 20, distress 20, flyby 10).

using System.Collections.Generic;
using GoF2Remake.Flight;
using NUnit.Framework;

namespace GoF2Remake.Tests
{
    public class CruiseEncountersTests
    {
        static CruiseEncounters.Entry E(string id, int weight, int minRank = 0, int[] security = null, bool freePlayOnly = false) =>
            new CruiseEncounters.Entry { id = id, kind = id, weight = weight, minRank = minRank, security = security, freePlayOnly = freePlayOnly };

        /// <summary>A random source giving the values in turn (the last one again after).</summary>
        static System.Func<float> Seq(params float[] values)
        {
            int i = 0;
            return () => values[i < values.Length ? i++ : values.Length - 1];
        }

        [Test]
        public void TheBuiltInTable()
        {
            var t = CruiseEncounters.BuiltIn();
            Assert.AreEqual(5, t.Count);
            int total = 0;
            foreach (var e in t) total += e.weight;
            Assert.AreEqual(100, total);
            Assert.IsTrue(t.Find(e => e.id == CruiseEncounters.Pirates).StopsTheDrive);
            Assert.IsFalse(t.Find(e => e.id == CruiseEncounters.Flyby).StopsTheDrive);
            Assert.IsTrue(t.Find(e => e.id == CruiseEncounters.Trader).visible);
            Assert.IsFalse(t.Find(e => e.id == CruiseEncounters.Pirates).visible);
            Assert.AreEqual(2, t.Find(e => e.id == CruiseEncounters.Distress).minRank);
        }

        [Test]
        public void PickIsByWeight()
        {
            var t = new List<CruiseEncounters.Entry> { E("a", 30), E("b", 20), E("c", 0), E("d", 50) };
            Assert.AreEqual("a", CruiseEncounters.Pick(t, null, 0f).id);
            Assert.AreEqual("a", CruiseEncounters.Pick(t, null, 0.299f).id);
            Assert.AreEqual("b", CruiseEncounters.Pick(t, null, 0.3f).id);
            Assert.AreEqual("d", CruiseEncounters.Pick(t, null, 0.5f).id);
            Assert.AreEqual("d", CruiseEncounters.Pick(t, null, 1f).id, "1 still picks the last");
            Assert.AreEqual("b", CruiseEncounters.Pick(t, e => e.id != "a", 0.1f).id, "without a: 0.1 x 70 = 7 < 20: b");
            Assert.AreEqual("d", CruiseEncounters.Pick(t, e => e.id != "a", 0.3f).id, "0.3 x 70 = 21 >= 20: d");
        }

        [Test]
        public void PickSkipsWhatIsNotAllowed()
        {
            var t = new List<CruiseEncounters.Entry> { E("a", 30), E("b", 20) };
            Assert.AreEqual("b", CruiseEncounters.Pick(t, e => e.id == "b", 0f).id);
            Assert.IsNull(CruiseEncounters.Pick(t, e => false, 0.5f));
            Assert.IsNull(CruiseEncounters.Pick(new List<CruiseEncounters.Entry>(), null, 0.5f));
        }

        [Test]
        public void AnEntrysOwnConditions()
        {
            Assert.IsTrue(CruiseEncounters.CanHappen(E("a", 10), 0, 3, false));
            Assert.IsFalse(CruiseEncounters.CanHappen(E("a", 0), 0, 3, false), "no weight");
            Assert.IsFalse(CruiseEncounters.CanHappen(E("a", 10, minRank: 4), 3, 0, false));
            Assert.IsTrue(CruiseEncounters.CanHappen(E("a", 10, minRank: 4), 4, 0, false));
            Assert.IsFalse(CruiseEncounters.CanHappen(E("a", 10, security: new[] { 0, 1 }), 9, 2, false));
            Assert.IsTrue(CruiseEncounters.CanHappen(E("a", 10, security: new[] { 0, 1 }), 9, 1, false));
            Assert.IsTrue(CruiseEncounters.CanHappen(E("a", 10, security: new int[0]), 9, 3, false), "an empty list is any");
            Assert.IsFalse(CruiseEncounters.CanHappen(E("a", 10, freePlayOnly: true), 9, 3, false));
            Assert.IsTrue(CruiseEncounters.CanHappen(E("a", 10, freePlayOnly: true), 9, 3, true));
            Assert.IsFalse(CruiseEncounters.CanHappen(null, 9, 3, true));
        }

        [Test]
        public void NothingInTheFirstSixSecondsThenARollEveryFiveSeconds()
        {
            var c = new CruiseEncounters(new List<CruiseEncounters.Entry> { E("a", 10) });
            var always = Seq(0f);   // the roll passes (0 < 10 %), the pick takes the first
            Assert.IsNull(c.Update(5999f, true, null, always));
            Assert.IsNull(c.Update(1f, true, null, always), "6000 ms: the check interval starts counting now");
            Assert.IsNull(c.Update(4999f, true, null, always));
            Assert.AreEqual("a", c.Update(1f, true, null, always).id);
            Assert.AreEqual(1, c.Count);
        }

        [Test]
        public void TheRollFailsNinetyPercentOfTheTime()
        {
            var c = new CruiseEncounters(new List<CruiseEncounters.Entry> { E("a", 10) });
            c.Update(6000f, true, null, Seq(0.5f));
            Assert.IsNull(c.Update(5000f, true, null, Seq(0.1f)), "0.1 x 100 = 10 is not below 10");
            Assert.IsNull(c.Update(5000f, true, null, Seq(0.5f)));
            Assert.AreEqual("a", c.Update(5000f, true, null, Seq(0.099f, 0f)).id);
        }

        [Test]
        public void ACooldownFollowsAnEncounterAndTwoPerOrbitAtMost()
        {
            var c = new CruiseEncounters(new List<CruiseEncounters.Entry> { E("a", 10) });
            var always = Seq(0f);
            c.Update(6000f, true, null, always);
            Assert.AreEqual("a", c.Update(5000f, true, null, always).id);
            Assert.IsNull(c.Update(89999f, true, null, always), "the cooldown");
            Assert.IsNull(c.Update(1f, true, null, always), "the cooldown is over; the interval counts from now");
            Assert.AreEqual("a", c.Update(5000f, true, null, always).id);
            Assert.AreEqual(2, c.Count);
            Assert.IsNull(c.Update(200000f, true, null, always), "two per orbit");
            Assert.IsNull(c.Update(5000f, true, null, always));
        }

        [Test]
        public void TheCruiseClockRestartsWhenTheDriveStops()
        {
            var c = new CruiseEncounters(new List<CruiseEncounters.Entry> { E("a", 10) });
            var always = Seq(0f);
            c.Update(5000f, true, null, always);
            Assert.IsNull(c.Update(16f, false, null, always), "stopped: the clock resets");
            Assert.IsNull(c.Update(5000f, true, null, always), "only 5 s of the new cruise");
            Assert.IsNull(c.Update(1000f, true, null, always));
            Assert.AreEqual("a", c.Update(5000f, true, null, always).id);
        }

        [Test]
        public void TheCooldownRunsWhileNotCruising()
        {
            var c = new CruiseEncounters(new List<CruiseEncounters.Entry> { E("a", 10) });
            var always = Seq(0f);
            c.Update(6000f, true, null, always);
            c.Update(5000f, true, null, always);
            c.Update(90000f, false, null, always);
            c.Update(6000f, true, null, always);
            Assert.AreEqual("a", c.Update(5000f, true, null, always).id);
        }

        [Test]
        public void NullTableMeansTheBuiltIns()
        {
            var c = new CruiseEncounters(null);
            Assert.AreEqual(5, c.Table.Count);
        }
    }
}
