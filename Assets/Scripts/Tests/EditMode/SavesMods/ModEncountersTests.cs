// ModEncountersTests.cs
// Feature: Modding/README.md "Cruise drives" > encounters.json (ModEncounters): a mod's entries join the game's table with
// "mod:id" ids and their fields, an "override" changes a built-in's weight / visibility / rank / security, a bad kind or a
// quest without its graph breaks the mod, an unknown field or override is a warning; the rift is the remake's one entry and
// the mods' rift entries only add bosses to its pool.

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
        public void AnEncounterMayHaveItsOwnMusic()
        {
            ModOn(@"[ { 'id': 'h', 'kind': 'highrisk', 'music': 'my_fight' }, { 'id': 'r', 'kind': 'rift', 'music': 'my_rift', 'bosses': [ { 'ship': '14' } ] } ]");
            Assert.IsFalse(AllMessages(Installed(Mod)).Contains("unknown field"), AllMessages(Installed(Mod)));
            Assert.AreEqual("my_fight", ModEncounters.All().Find(e => e.kind == "highrisk").music);
            Assert.AreEqual("my_rift", ModEncounters.All().Find(e => e.kind == "rift").music, "a rift entry's music is the rift's");
        }

        [Test]
        public void AModMayUseEverspaceTwosKinds()
        {
            ModOn(@"[ { 'id': 'b', 'kind': 'battle' }, { 'id': 'h', 'kind': 'highrisk', 'ships': 3 }, { 'id': 'l', 'kind': 'lootdrone' }, " +
                  "{ 'id': 'r', 'kind': 'rift', 'bosses': [ { 'ship': 'x:big', 'raiders': 7.7 }, { 'ship': '14' } ] } ]");
            Assert.IsFalse(Installed(Mod).Broken, AllMessages(Installed(Mod)));
            Assert.AreEqual(9, ModEncounters.All().Count, "the three and the remake's rift");
            var rift = ModEncounters.All().Find(e => e.kind == "rift");
            Assert.AreEqual(2, rift.bosses.Count);
            Assert.AreEqual(("x:big", 7.7f), (rift.bosses[0].ship, rift.bosses[0].raiders));
            Assert.AreEqual(8f, rift.bosses[1].raiders, "the default"); 
            Assert.AreEqual(3, ModEncounters.All().Find(e => e.kind == "highrisk").ships);
        }

        [Test]
        public void TheRiftIsTheRemakesAndModsOnlyAddBosses()
        {
            WriteMod("enc", Mod, "1.0", "", ("encounters.json", @"[ { 'id': 'r', 'kind': 'rift', 'bosses': [ { 'ship': 'x:a', 'raiders': 7 } ] } ]"));
            WriteMod("enc2", "t_enc2", "1.0", "", ("encounters.json",
                @"[ { 'id': 'r2', 'kind': 'rift', 'weight': 9, 'bosses': [ { 'ship': 'y:b', 'raiders': 12, 'race': 'void', 'weapon': 'y:gun', 'secondary': '31' }, { 'ship': 'y:c' } ] }, { 'override': 'rift', 'weight': 50 } ]"));
            ScanAndEnable(Mod, "t_enc2");
            var all = ModEncounters.All();
            var rifts = all.FindAll(e => e.kind == CruiseEncounters.Rift);
            Assert.AreEqual(1, rifts.Count, "one rift encounter");
            var own = CruiseEncounters.RiftEntry();
            Assert.AreEqual((CruiseEncounters.Rift, own.weight, own.minRank, own.ships), (rifts[0].id, rifts[0].weight, rifts[0].minRank, rifts[0].ships),
                            "the remake's chance, rank and stages: neither a mod's weight nor an override changes them");
            CollectionAssert.AreEqual(new[] { "x:a", "y:b", "y:c" }, rifts[0].bosses.Select(b => b.ship).ToArray(), "one pool");
            Assert.AreEqual(null, rifts[0].bosses[0].race, "the stage's side");
            Assert.AreEqual("void", rifts[0].bosses[1].race, "the race a boss flies for");
            Assert.AreEqual(("y:gun", "31"), (rifts[0].bosses[1].weapon, rifts[0].bosses[1].secondary), "a fighter boss's own gun and missiles");
            var warnings = string.Join(" | ", Installed("t_enc2").Warnings);
            StringAssert.Contains("weight", warnings);
            StringAssert.Contains("overridden", warnings);
            ModManager.SetEnabled(Mod, false);
            ModManager.SetEnabled("t_enc2", false);
            Assert.IsFalse(ModEncounters.All().Any(e => e.kind == CruiseEncounters.Rift), "no mod rift bosses, no rift");
        }

        [Test]
        public void WithoutModsARiftStageIsPiratesOrTheVoid()
        {
            ScanAndEnable();
            CollectionAssert.AreEquivalent(new[] { GoF2Remake.Flight.Standing.Pirate, GoF2Remake.Flight.Standing.Void }, GoF2Remake.World.RiftEncounter.Sides());
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
