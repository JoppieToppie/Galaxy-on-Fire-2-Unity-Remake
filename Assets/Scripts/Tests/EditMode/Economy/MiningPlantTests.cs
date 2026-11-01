// MiningPlantTests.cs
// Feature: the Mido Mining Plant at Coromesk (103) turns ore into cores (MiningPlant).
// Expected: PlayerEgo::isDockedToMiningPlant 0xadcb8 (station 0x67, Mission::isEmpty, not the alien orbit), MGame::OnUpdate
// 0x1ac778 (290 with the ratio, the ores in the hold or 286), MGame::OnTouchEnd 0x1a98d8 (ores 0x9a..0xa4 -> +0xb, Novanium
// 0xd9 -> 0xda, 30 t per core, Status::hardCoreMode 100 t; 291 + the cores, or 292).

using GoF2Remake.Data;
using NUnit.Framework;

namespace GoF2Remake.Tests
{
    public class MiningPlantTests : EconomyTest
    {
        [Test]
        public void EveryOreHasItsCore()
        {
            Assert.AreEqual(165, MiningPlant.CoreOf(154), "gold");
            Assert.AreEqual(175, MiningPlant.CoreOf(164), "Void crystals -> Void essence");
            Assert.AreEqual(218, MiningPlant.CoreOf(217), "novanium");
            Assert.AreEqual(-1, MiningPlant.CoreOf(165), "a core isn't converted");
            Assert.AreEqual(-1, MiningPlant.CoreOf(153));
            for (int ore = 154; ore <= 164; ore++)
            {
                Assert.AreEqual(MiningPlant.OreCategory, Db.Item(ore).categoryId, $"item {ore}");
                Assert.AreEqual(24, Db.Item(MiningPlant.CoreOf(ore)).categoryId, $"core of {ore}");
            }
        }

        [Test]
        public void OfferedOnlyAtCoromeskWithoutACampaignMission()
        {
            Session.StoryMission = new StoryMission();
            Assert.IsTrue(MiningPlant.Offered(103, false));
            Assert.IsFalse(MiningPlant.Offered(103, true), "the alien orbit");
            Assert.IsFalse(MiningPlant.Offered(102, false));
            Session.StoryMission = new StoryMission { type = StoryType.AmountReached, station = 103, goodsItem = 155, goodsAmount = 30 };
            Assert.IsFalse(MiningPlant.Offered(103, false), "135's titanium goes in through the drop-off");
        }

        [Test]
        public void ThirtyTonsMakeOneCoreAndTheRestStays()
        {
            Shop.AddToCargo(217, 62);
            Shop.AddToCargo(154, 29);
            Shop.AddToCargo(156, 30);
            var made = MiningPlant.Convert();
            Assert.AreEqual(2, made.Count);
            Assert.AreEqual(167, made[0].item); Assert.AreEqual(1, made[0].amount);
            Assert.AreEqual(218, made[1].item); Assert.AreEqual(2, made[1].amount);
            Assert.AreEqual(2, Shop.CargoOf(217));
            Assert.AreEqual(29, Shop.CargoOf(154), "less than 30 t stays ore");
            Assert.AreEqual(0, Shop.CargoOf(156));
            Assert.AreEqual(2, Shop.CargoOf(218));
            Assert.AreEqual(1, Shop.CargoOf(167));
            StringAssert.StartsWith(Localization.Get(291), MiningPlant.Result(made));
        }

        [Test]
        public void ExtremeNeedsAHundredTons()
        {
            Session.Difficulty = Session.DifficultyExtreme;
            Assert.AreEqual(100, MiningPlant.Ratio);
            Shop.AddToCargo(217, 99);
            var made = MiningPlant.Convert();
            Assert.AreEqual(0, made.Count);
            Assert.AreEqual(99, Shop.CargoOf(217));
            Assert.AreEqual(Localization.Get(292), MiningPlant.Result(made));
        }

        [Test]
        public void TheQuestionListsTheOreInTheHold()
        {
            string empty = MiningPlant.Question(Db);
            StringAssert.Contains("30", empty);
            StringAssert.EndsWith(Localization.Get(286), empty);
            Shop.AddToCargo(217, 62);
            StringAssert.EndsWith("62t " + GameNames.Item(217), MiningPlant.Question(Db));
        }
    }
}
