// ModEncountersTests.cs
// Feature: Modding/README.md "Cruise drives" > encounters.json (ModEncounters): a mod's entries join the game's table with
// "mod:id" ids and their fields, an "override" changes a built-in's weight / visibility / rank / security, a bad kind or a
// quest without its graph breaks the mod, an unknown field or override is a warning.

using System.Linq;
using GoF2Remake.Flight;
using GoF2Remake.Modding;
using NUnit.Framework;

namespace GoF2Remake.Tests
{
    [Unity.Scripting.LifecycleManagement.NoAutoStaticsCleanup]
    public class ModEncountersTests : SavesModsTest
    {
        const string Mod = "t_enc";

        void ModOn(string json)
        {
            WriteMod("enc", Mod, "1.0", "", ("encounters.json", json));
            ScanAndEnable(Mod);
        }

        [Test]
        public void WithoutModsTheTableIsTheGamesOwn()
        {
            ScanAndEnable();
            var all = ModEncounters.All();
            Assert.AreEqual(5, all.Count);
            Assert.AreEqual(30, all.Find(e => e.id == CruiseEncounters.Pirates).weight);
        }

        [Test]
        public void AModAddsEntriesAndOverridesBuiltIns()
        {
            ModOn(@"[
                { 'id': 'ambush', 'kind': 'Pirates', 'weight': 25, 'minRank': 4, 'security': [0, 1], 'ships': 4, 'name': { 'en': 'Raider pack', 'de': 'Räuberrudel' } },
                { 'id': 'beacon', 'kind': 'quest', 'quest': 'my_beacon', 'weight': 5, 'visible': true },
                { 'override': 'flyby', 'weight': 0 },
                { 'override': 'trader', 'visible': false, 'minRank': 3 }
            ]");
            Assert.IsFalse(Installed(Mod).Broken, AllMessages(Installed(Mod)));
            var all = ModEncounters.All();
            Assert.AreEqual(7, all.Count);
            var ambush = all.Find(e => e.id == Mod + ":ambush");
            Assert.IsNotNull(ambush);
            Assert.AreEqual(CruiseEncounters.Pirates, ambush.kind, "kinds read in any case");
            Assert.AreEqual(25, ambush.weight);
            Assert.AreEqual(4, ambush.minRank);
            CollectionAssert.AreEqual(new[] { 0, 1 }, ambush.security);
            Assert.AreEqual(4, ambush.ships);
            Assert.IsFalse(ambush.visible);
            Assert.AreEqual("Raider pack", ambush.name);
            var beacon = all.Find(e => e.id == Mod + ":beacon");
            Assert.AreEqual(CruiseEncounters.Quest, beacon.kind);
            Assert.AreEqual("my_beacon", beacon.quest);
            Assert.IsTrue(beacon.visible);
            Assert.IsFalse(beacon.StopsTheDrive);
            Assert.AreEqual(0, all.Find(e => e.id == CruiseEncounters.Flyby).weight, "overridden");
            var trader = all.Find(e => e.id == CruiseEncounters.Trader);
            Assert.IsFalse(trader.visible);
            Assert.AreEqual(3, trader.minRank);
            Assert.AreEqual(20, trader.weight, "untouched fields stay");
            Assert.IsEmpty(Installed(Mod).Warnings, AllMessages(Installed(Mod)));
        }

        [Test]
        public void TheTableFollowsTheModsOnAndOff()
        {
            ModOn(@"[ { 'id': 'x', 'kind': 'derelict' } ]");
            Assert.AreEqual(6, ModEncounters.All().Count);
            ModManager.SetEnabled(Mod, false);
            Assert.AreEqual(5, ModEncounters.All().Count);
        }

        [Test]
        public void AModMayUseEverspaceTwosKinds()
        {
            ModOn(@"[ { 'id': 'b', 'kind': 'battle' }, { 'id': 'h', 'kind': 'highrisk', 'ships': 3 }, { 'id': 'l', 'kind': 'lootdrone' } ]");
            Assert.IsFalse(Installed(Mod).Broken, AllMessages(Installed(Mod)));
            Assert.AreEqual(8, ModEncounters.All().Count);
            Assert.AreEqual(3, ModEncounters.All().Find(e => e.kind == "highrisk").ships);
        }

        [Test]
        public void ABadKindBreaksTheMod()
        {
            ModOn(@"[ { 'id': 'x', 'kind': 'dragons' } ]");
            Assert.IsTrue(Installed(Mod).Broken);
            StringAssert.Contains("kind", AllMessages(Installed(Mod)));
        }

        [Test]
        public void AQuestEntryNeedsItsGraph()
        {
            ModOn(@"[ { 'id': 'x', 'kind': 'quest' } ]");
            Assert.IsTrue(Installed(Mod).Broken);
            StringAssert.Contains("quest", AllMessages(Installed(Mod)));
        }

        [Test]
        public void IdOrOverrideNotBoth()
        {
            ModOn(@"[ { 'id': 'x', 'override': 'flyby', 'kind': 'flyby' } ]");
            Assert.IsTrue(Installed(Mod).Broken);
        }

        [Test]
        public void UnknownFieldsAndOverridesWarn()
        {
            ModOn(@"[ { 'id': 'x', 'kind': 'flyby', 'colour': 'red' }, { 'override': 'nothing', 'weight': 1 } ]");
            Assert.IsFalse(Installed(Mod).Broken, AllMessages(Installed(Mod)));
            ModEncounters.All();
            var warnings = string.Join(" | ", Installed(Mod).Warnings);
            StringAssert.Contains("colour", warnings);
            StringAssert.Contains("nothing", warnings);
        }

        [Test]
        public void InterstellarSaysWhereItPullsTheShipOut()
        {
            ModOn(@"[ { 'id': 'x', 'kind': 'trader', 'interstellar': 'System' }, { 'override': 'pirates', 'interstellar': 'never' } ]");
            Assert.IsFalse(Installed(Mod).Broken, AllMessages(Installed(Mod)));
            var all = ModEncounters.All();
            Assert.AreEqual(CruiseEncounters.AtSystem, all.First(e => e.id == Mod + ":x").interstellar);
            Assert.AreEqual(CruiseEncounters.Never, all.First(e => e.id == CruiseEncounters.Pirates).interstellar);
            Assert.IsFalse(Installed(Mod).Warnings.Any(w => w.Contains("interstellar")), AllMessages(Installed(Mod)));
        }

        [Test]
        public void InterstellarMustBeAPlace()
        {
            ModOn(@"[ { 'id': 'x', 'kind': 'flyby', 'interstellar': 'moon' } ]");
            Assert.IsTrue(Installed(Mod).Broken);
            StringAssert.Contains("interstellar", AllMessages(Installed(Mod)));
            ModManager.SetEnabled(Mod, false);
        }

        [Test]
        public void SecurityMustBeLevels()
        {
            ModOn(@"[ { 'id': 'x', 'kind': 'flyby', 'security': [0, 7] } ]");
            Assert.IsTrue(Installed(Mod).Broken);
            ModManager.SetEnabled(Mod, false);
        }
    }
}
