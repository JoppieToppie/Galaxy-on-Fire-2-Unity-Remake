// ModMedalsTests.cs
// Feature: Modding/README.md "Medals" (Modding.ModMedals, a player's request on Discord 2026-10-23): a mod's medals.json adds
// medals earned by a counter against thresholds or by conditions, at the game's tiers (bronze, silver, gold, supernova) or
// tiers of its own (platinum between gold and supernova); a medal keeps its best tier (Session.ModMedals, saved); its
// unlocks learn a blueprint, put a ship on sale at a dealer, give items; the conditions read them (medal(key[, tier]),
// medals(tier)) and the new counters (killswith(item), killed(ship), stat(name)). None in a game without medals. The test
// wires GameHooks.Condition / Number to the event graphs' expressions as the game's NetHooksInstaller does.

using System.Collections.Generic;
using GoF2Remake.Data;
using GoF2Remake.Events;
using GoF2Remake.Modding;
using NUnit.Framework;

namespace GoF2Remake.Tests
{
    public class ModMedalsTests : SavesModsTest
    {
        GameHooks.ConditionCheck savedCondition;
        GameHooks.NumberCheck savedNumber;

        [SetUp]
        public void WireConditions()
        {
            savedCondition = GameHooks.Condition;
            savedNumber = GameHooks.Number;
            GameHooks.Condition = EventRunner.Condition;
            GameHooks.Number = EventRunner.Number;
        }

        [TearDown]
        public void UnwireConditions()
        {
            GameHooks.Condition = savedCondition;
            GameHooks.Number = savedNumber;
        }

        Database Load()
        {
            WriteMod("md", "t_med", "1.0", "", ("medals.json", @"{
                'tiers': [ { 'id': 'platinum', 'name': 'Platinum', 'rank': 3.5, 'color': '#e5e4e2', 'reward': 10000 },
                           { 'id': 'bronze', 'name': 'Mine' } ],
                'medals': [
                    { 'id': 'hunter', 'name': 'Hunter', 'hint': 'Destroy # ships.', 'icon': 4, 'counter': 'kills',
                      'grades': { 'gold': 5, 'bronze': 2, 'platinum': 10 }, 'reward': { 'platinum': 20000 },
                      'unlocks': [ { 'tier': 'gold', 'blueprint': 232 }, { 'tier': 'platinum', 'ship': 8, 'stations': [10] },
                                   { 'item': 2, 'amount': 1 } ] },
                    { 'id': 'rich', 'name': { 'en': 'Rich', 'de': 'Reich' }, 'hidden': true, 'grades': { 'gold': 'credits >= 1000' } },
                    { 'id': 'odd', 'grades': { 'diamond': 3, 'silver': 4 } },
                    { 'name': 'No id', 'grades': { 'gold': 'credits > 0' } }
                ] }"));
            ScanAndEnable("t_med");
            Assert.IsFalse(Installed("t_med").Broken, AllMessages(Installed("t_med")));
            return Database.Load();
        }

        static ModMedals.Medal Hunter => ModMedals.Find("t_med:hunter");
        static int HunterIndex => ModMedals.IndexOf(Hunter);

        [Test]
        public void MedalsAndTiersAreRead()
        {
            Load();
            Assert.AreEqual(2, ModMedals.All.Count, "hunter and rich; odd has no grade it can be earned at, the last no id");
            var h = Hunter;
            CollectionAssert.AreEqual(new[] { "bronze", "gold", "platinum" }, h.grades.ConvertAll(g => g.tier.id), "by rank, platinum above gold");
            Assert.AreEqual(Achievements.Count, HunterIndex, "the mods' medals are numbered after the game's 45");
            Assert.AreEqual("Hunter", Achievements.Name(HunterIndex));
            Assert.AreEqual(3, h.unlocks.Count);
            var platinum = ModMedals.TierOf("platinum");
            Assert.AreEqual(3.5f, platinum.rank);
            Assert.IsTrue(platinum.tintPlate, "no plate: the silver plate tinted in its colour");
            Assert.AreEqual("Bronze", ModMedals.TierOf("bronze").Name, "a mod can't take a game tier's id");
            string all = AllMessages(Installed("t_med"));
            StringAssert.Contains("is one of the game's tiers", all);
            StringAssert.Contains("a number needs the medal's \"counter\"", all);
            StringAssert.Contains("no tier \"diamond\"", all);
            StringAssert.Contains("a medal needs an \"id\"", all);
        }

        [Test]
        public void ACounterEarnsItsTiersOneAfterAnotherWithTheirUnlocks()
        {
            var db = Load();
            Session.Kills = 1;
            CollectionAssert.IsEmpty(ModMedals.Check(db));
            Assert.IsNull(ModMedals.Earned(Hunter));
            Assert.IsTrue(ModMedals.Next(Hunter, out var next, out long count, out long target, out float fraction));
            Assert.AreEqual(("bronze", 1L, 2L, 0.5f), (next.id, count, target, fraction));

            Session.Kills = 3;
            Session.Cargo.Clear();
            Assume.That(Shop.FreeCargo(db), Is.GreaterThan(0));
            CollectionAssert.Contains(Achievements.Check(db), HunterIndex, "bronze, through the game's check on docking");
            Assert.AreEqual("bronze", ModMedals.Earned(Hunter).id);
            Assert.AreEqual(1000, ModMedals.Reward(Hunter), "the tier's reward");
            Assert.IsTrue(Session.Cargo.Exists(c => c.item == 2 && c.amount == 1), "the bronze unlock's item in the hold");
            Assert.AreEqual(1, ModMedals.TakeNotes(HunterIndex).Count);
            Assert.IsFalse(Blueprints.IsUnlocked(232));
            StringAssert.StartsWith("Destroy 2 ships.", ModMedals.Hint(Hunter, null));
            StringAssert.Contains("Bronze 2 · Gold 5 · Platinum 10", ModMedals.Hint(Hunter, null));

            Session.Kills = 3;
            CollectionAssert.IsEmpty(ModMedals.Check(db), "nothing new");
            Session.Kills = 7;
            CollectionAssert.AreEqual(new[] { HunterIndex }, ModMedals.Check(db));
            Assert.AreEqual("gold", ModMedals.Earned(Hunter).id);
            Assert.IsTrue(Blueprints.IsUnlocked(232), "gold learns the fireworks");
            Assert.IsTrue(Session.Cargo.Exists(c => c.item == 2 && c.amount == 1), "the item is given once");
            var ships = new List<int> { 3 };
            ModMedals.AddShips(10, ships);
            CollectionAssert.AreEqual(new[] { 3 }, ships, "the VoidX waits for platinum");

            Session.Kills = 12;
            ModMedals.Check(db);
            Assert.AreEqual("platinum", ModMedals.Earned(Hunter).id);
            Assert.AreEqual(20000, ModMedals.Reward(Hunter), "the medal's own reward for the tier");
            ModMedals.AddShips(10, ships);
            CollectionAssert.AreEqual(new[] { 8, 3 }, ships, "on sale at Thynome, first");
            var elsewhere = new List<int> { 3 };
            ModMedals.AddShips(11, elsewhere);
            CollectionAssert.AreEqual(new[] { 3 }, elsewhere, "only at its stations");
            Assert.IsFalse(ModMedals.Next(Hunter, out _, out _, out _, out _), "at its top grade");

            Session.Kills = 0;
            ModMedals.Check(db);
            Assert.AreEqual("platinum", ModMedals.Earned(Hunter).id, "a medal keeps its best tier");
        }

        [Test]
        public void AConditionGradeAndAHiddenMedal()
        {
            var db = Load();
            var rich = ModMedals.Find("t_med:rich");
            Session.Credits = 10;
            ModMedals.Check(db);
            Assert.IsFalse(ModMedals.Shown(rich), "hidden until earned");
            Session.Credits = 5000;
            ModMedals.Check(db);
            Assert.AreEqual("gold", ModMedals.Earned(rich).id);
            Assert.IsTrue(ModMedals.Shown(rich));
            Assert.AreEqual(5000, ModMedals.Reward(rich));
        }

        [Test]
        public void ConditionsReadTheMedalsAndTheNewCounters()
        {
            var db = Load();
            bool Holds(string c)
            {
                bool r = EventRunner.Condition(c, out string error);
                Assert.IsNull(error, c);
                return r;
            }
            Assert.IsFalse(Holds("medal(t_med:hunter)"));
            Session.Kills = 7;
            ModMedals.Check(db);
            Assert.IsTrue(Holds("medal(t_med:hunter)"));
            Assert.IsTrue(Holds("medal(t_med:hunter, silver)"), "gold is above silver");
            Assert.IsFalse(Holds("medal(t_med:hunter, platinum)"));
            Assert.IsTrue(Holds("medals(gold) == 1"), "the mods' gold medals count with the game's");
            Assert.IsTrue(Holds("medals(platinum) == 0"));
            Assert.IsTrue(Holds("medals(off) == 0"));
            Session.Medals = new int[Achievements.Count];
            Session.Medals[4] = 2;
            Assert.IsTrue(Holds("medal(4, silver)"));
            Assert.IsFalse(Holds("medal(4, gold)"));
            Assert.IsFalse(EventRunner.Condition("medal(t_med:hunter, diamond)", out string bad));
            StringAssert.Contains("no tier \"diamond\"", bad);

            Session.KillTally["with:" + Session.ItemRef(2)] = 4;
            Session.KillTally["ship:" + Session.ShipRef(14)] = 2;
            Assert.IsTrue(Holds("killswith(2) == 4"));
            Assert.IsTrue(Holds("killed(14) == 2"));
            Assert.IsTrue(Holds("killed(3) == 0"));
            Session.OreMined = 120;
            Assert.IsTrue(EventRunner.Number("stat(ore) + 1", out double v, out _));
            Assert.AreEqual(121, v);
            Assert.IsFalse(EventRunner.Condition("stat(nope)", out string noStat));
            StringAssert.Contains("kills, piratekills", noStat);
        }

        [Test]
        public void NoneInAGameWithoutMedals()
        {
            var db = Load();
            Session.NoMedals = true;
            try
            {
                Session.Kills = 50;
                CollectionAssert.IsEmpty(Achievements.Check(db));
                CollectionAssert.IsEmpty(ModMedals.Check(db));
                Assert.IsNull(ModMedals.Earned(Hunter));
                Assert.IsTrue(EventRunner.Condition("medals(off)", out _));
            }
            finally { Session.NoMedals = false; }
        }

        [Test]
        public void TheMedalsAndTheKillTallyAreSavedAndCarriedIntoNewGamePlus()
        {
            var db = Load();
            Session.Kills = 3;
            ModMedals.Check(db);
            Session.ModMedals["gone_mod:old"] = "gold";   // a mod that is off: kept
            Session.KillTally["with:2"] = 5;
            Assert.IsTrue(SaveGame.Save(2));
            Session.ModMedals.Clear();
            Session.KillTally.Clear();
            Assert.IsTrue(SaveGame.Load(2));
            Assert.AreEqual("bronze", Session.ModMedals["t_med:hunter"]);
            Assert.AreEqual("gold", Session.ModMedals["gone_mod:old"]);
            Assert.AreEqual(5, Session.KillTally["with:2"]);
            Assert.AreEqual(1, ModMedals.Reached("gone_mod:old, gold", out _), "an earned medal of a mod that is off still counts");

            Session.ModMedals["t_med:hunter"] = "gold";
            ModMedals.Carry(new List<string> { "t_med:hunter|bronze", "t_med:rich|gold" });
            Assert.AreEqual("gold", Session.ModMedals["t_med:hunter"], "the better of the two");
            Assert.AreEqual("gold", Session.ModMedals["t_med:rich"]);
        }
    }
}
