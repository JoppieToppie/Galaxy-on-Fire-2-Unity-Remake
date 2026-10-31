// KhadorCronusGiftTests.cs
// #122: Khador's Cronus at Kothar (100) at step 77 is a gift once, whatever the economy (ModStation::OnTouchEnd 0xea4ec:
// Station::addShip(Ship::makeShip(ships[0x25], 0)), a dealer row priced 0; the Default economy lists the Cronus at
// 1 200 000, the Android one at 0). Its value stays the table's (a later trade-in), a traded-back Cronus is a row at the
// table price, and Session.KhadorCronusGift is saved (a save from before the flag gets it at 77 outside the Cronus).

using System.IO;
using System.Text.RegularExpressions;
using GoF2Remake.Data;
using NUnit.Framework;

namespace GoF2Remake.Tests
{
    public class KhadorCronusGiftTests : EconomyTest
    {
        const int Kothar = 100, Cronus = 37, Betty = 0;
        Database db;
        StationStock kothar;

        [SetUp]
        public void DefaultEconomyAtStep77()
        {
            Session.Economy = Economy.Default;
            db = Database.Load();
            Session.FreePlay = false;
            Session.CampaignMission = 77;
            Session.ShipIndex = Betty;
            Session.Equipment.Clear();
            Session.Credits = 0;
            kothar = Stock(Kothar);
            Session.RecentStations.Clear();
            Session.RecentStations.Add(kothar);
            Session.KhadorCronusGift = false;
        }

        [TearDown]
        public void ClearTheGift() => Session.KhadorCronusGift = false;

        [Test]
        public void TheGiftRowIsFreeOnceAndTheCronusKeepsItsValue()
        {
            Story.AfterDockedAdvance(77, Kothar);
            Assert.IsTrue(Session.KhadorCronusGift);
            CollectionAssert.Contains(kothar.ships, Cronus);
            var h = new Hangar(db, kothar);
            int value = h.ShipPrice(Cronus);
            Assert.Greater(value, 1000000, "the Default economy's Cronus (1 200 000)");
            Assert.AreEqual(0, h.BuyShipPrice(Cronus), "makeShip(37, 0): the gift row is free");

            int betty = h.ShipPrice(Betty);
            Assert.IsTrue(h.BuyShip(Cronus));
            Assert.AreEqual(Cronus, Session.ShipIndex);
            Assert.AreEqual(betty, Session.Credits, "the Betty's trade-in, nothing paid for the Cronus");
            Assert.IsFalse(Session.KhadorCronusGift, "taken");

            // Bought back: the Betty for the Cronus' table value, and the Cronus row is priced again (no credit loop).
            Assert.IsTrue(h.BuyShip(Betty));
            Assert.AreEqual(betty + value - h.BuyShipPrice(Betty), Session.Credits);
            Assert.AreEqual(value, h.BuyShipPrice(Cronus));
        }

        [Test]
        public void OnlyAtKotharDuringStep77()
        {
            Session.KhadorCronusGift = true;
            var h = new Hangar(db, kothar);
            Assert.AreEqual(0, h.BuyShipPrice(Cronus));
            Session.CampaignMission = 78;
            Assert.AreEqual(h.ShipPrice(Cronus), h.BuyShipPrice(Cronus), "after the step");
            Session.CampaignMission = 77;
            var dis = new Hangar(db, Stock(70));
            Assert.AreEqual(dis.ShipPrice(Cronus), dis.BuyShipPrice(Cronus), "another station");
            Session.FreePlay = true;
            Assert.AreEqual(h.ShipPrice(Cronus), h.BuyShipPrice(Cronus), "no campaign");
        }

        [Test]
        public void TheGiftIsSavedAndOldSavesAt77GetIt()
        {
            Session.KhadorCronusGift = true;
            Assert.IsTrue(SaveGame.Save(1));
            Session.KhadorCronusGift = false;
            Assert.IsTrue(SaveGame.Load(1));
            Assert.IsTrue(Session.KhadorCronusGift, "saved");

            Session.KhadorCronusGift = false;
            Assert.IsTrue(SaveGame.Save(1));
            Assert.IsTrue(SaveGame.Load(1));
            Assert.IsFalse(Session.KhadorCronusGift, "taken stays taken");

            // A save from before the flag: at 77 and not in the Cronus, the row was added unpriced.
            string path = SaveGame.SlotPath(1);
            string text = File.ReadAllText(path);
            Assert.IsTrue(Regex.IsMatch(text, "\"khadorCronusGift\":\\s*0,?"));
            File.WriteAllText(path, Regex.Replace(text, "\"khadorCronusGift\":\\s*0,?\\s*", ""));
            Assert.IsTrue(SaveGame.Load(1));
            Assert.IsTrue(Session.KhadorCronusGift, "an older save at 77 outside the Cronus");
        }

        [Test]
        public void OnlyASaveAtStep77KeepsTheJumpDriveLocked()
        {
            // #218: a save from before 2026-10-10 kept step 77's lock after the Valkyrie campaign, on every Khador Drive.
            Session.Equipment.Add(new ItemStack(GalaxyMap.KhadorDriveItem, 1));
            Session.Equipment.Add(new ItemStack(GalaxyMap.KhadorDriveItem, 1));
            Session.Unsaleable.Add(GalaxyMap.KhadorDriveItem);
            Session.Unsaleable.Add(131);   // another story item keeps its lock
            Assert.IsTrue(SaveGame.Save(1));
            Assert.IsTrue(SaveGame.Load(1));
            Assert.IsFalse(Hangar.IsSaleable(GalaxyMap.KhadorDriveItem), "step 77: locked");

            Session.CampaignMission = 100;
            Session.Unsaleable.Add(GalaxyMap.KhadorDriveItem);
            Assert.IsTrue(SaveGame.Save(1));
            Assert.IsTrue(SaveGame.Load(1));
            Assert.IsTrue(Hangar.IsSaleable(GalaxyMap.KhadorDriveItem), "past the Valkyrie campaign: free again");
            Assert.IsFalse(Hangar.IsSaleable(131));
        }
    }
}
