// CruiseEncounterRulesTests.cs
// Feature: docs/notes/navigation.md "Cruise drive" > encounters: where they may come (CruiseEncounterRules.Allowed): not the
// alien orbit, the special stations (10, 100-104, 108), Loma's black market 25 and the Void / supernova systems 27, 32, 33,
// an unknown system; a plain orbit of the map is fine.

using GoF2Remake.Data;
using NUnit.Framework;

namespace GoF2Remake.Tests
{
    [Unity.Scripting.LifecycleManagement.NoAutoStaticsCleanup]
    public class CruiseEncounterRulesTests : EconomyTest
    {
        [Test]
        public void APlainOrbitAllowsThem()
        {
            var st = Db.Stations.Find(s => s.index == 0);
            Assert.IsNotNull(st);
            Assert.IsTrue(CruiseEncounterRules.Allowed(Db, st.index, st.system, false));
        }

        [Test]
        public void TheSpecialPlacesDoNot()
        {
            Assert.IsFalse(CruiseEncounterRules.Allowed(Db, 0, 0, true), "the alien orbit");
            foreach (int station in new[] { 10, 100, 101, 102, 103, 104, 108 })
                Assert.IsFalse(CruiseEncounterRules.Allowed(Db, station, 0, false), "station " + station);
            foreach (int system in new[] { 25, 27, 32, 33 })
                Assert.IsFalse(CruiseEncounterRules.Allowed(Db, 0, system, false), "system " + system);
            Assert.IsFalse(CruiseEncounterRules.Allowed(Db, 0, 999, false), "no such system");
            Assert.IsFalse(CruiseEncounterRules.Allowed(null, 0, 0, false));
        }
    }
}
