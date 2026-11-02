// CheatsTests.cs
// Feature: CLAUDE.md "UI and platforms" > Debug panel, the plain C# actions of Cheats: credits (capped), repair (the saved
// pools back to full, the gamma pool refilled), reveal all systems, make peace, give items, Give and mount (the hangar's
// mount / swap rules), fill the Kaamo Club (owned, one of every storable item, 50 of each secondary, one hull of every ship
// a player can own), energy cells, refill ammo.
// Expected: the rules as CLAUDE.md states them (the Kaamo fill skips 13 / 14 / 15, the ship flown and the hulls without a
// model, 50 / 53: 64 - 6 = 58 hulls; the storage refuses the story's unsaleable items, 323), shop.md 6 for the mounting.

using System.Linq;
using GoF2Remake.Data;
using NUnit.Framework;

namespace GoF2Remake.Tests
{
    public class CheatsTests : EconomyTest
    {
        [Test]
        public void AddCreditsIsCappedBelowABillion()
        {
            Session.Credits = 5;
            Cheats.AddCredits(100000);
            Assert.AreEqual(100005, Session.Credits);
            Session.Credits = 999990000;
            Cheats.AddCredits(1000000);
            Assert.AreEqual(999999999, Session.Credits);
        }

        [Test]
        public void RepairFillsThePools()
        {
            Session.PlayerHull = 40;
            Session.PlayerArmor = 3;
            Session.PlayerShield = 12.5f;
            Session.PlayerGamma = 20f;
            Cheats.Repair();
            Assert.AreEqual(-1, Session.PlayerHull, "-1 = full");
            Assert.AreEqual(-1, Session.PlayerArmor);
            Assert.AreEqual(-1f, Session.PlayerShield);
            Assert.AreEqual(100f, Session.PlayerGamma);
        }

        [Test]
        public void RepairLeavesAnUnusedGammaPoolAlone()
        {
            Session.PlayerGamma = -1f;
            Cheats.Repair();
            Assert.AreEqual(-1f, Session.PlayerGamma);
        }

        [Test]
        public void RevealAllSystems()
        {
            Session.SystemVisible = new bool[34];
            Session.SystemVisible[3] = true;
            Cheats.RevealAllSystems();
            Assert.IsTrue(Session.SystemVisible.All(v => v));
            Session.SystemVisible = null;
            Assert.DoesNotThrow(Cheats.RevealAllSystems);
        }

        [Test]
        public void MakePeace()
        {
            Session.Standing = new[] { -90, 85 };
            Session.AttackedStations.Add(70);
            Cheats.MakePeace();
            CollectionAssert.AreEqual(new[] { 0, 0 }, Session.Standing);
            Assert.IsEmpty(Session.AttackedStations);
        }

        [Test]
        public void GiveItemAndEnergyCells()
        {
            Cheats.GiveItem(5, 3);
            Cheats.AddEnergyCells(20);
            Cheats.AddEnergyCells(5);
            Assert.AreEqual(3, Shop.CargoOf(5));
            Assert.AreEqual(25, Shop.CargoOf(122));
            Assert.AreEqual(122, GalaxyMap.EnergyCellItem);
        }

        [Test]
        public void RefillAmmoTopsUpEverySecondaryToFifty()
        {
            Session.Equipment.Add(new ItemStack(37, 70));
            Cheats.RefillAmmo(Db);
            Assert.AreEqual(50, Session.Equipment.Find(e => e.item == 36).amount);
            Assert.AreEqual(70, Session.Equipment.Find(e => e.item == 37).amount, "never lowered");
            Assert.AreEqual(1, Session.Equipment.Find(e => e.item == 54).amount, "only secondaries");
        }

        // ---- Give and mount -------------------------------------------------------------------------------------------

        [Test]
        public void GiveAndMountUndockedOnlyFillsTheHold()
        {
            Cheats.GiveAndMount(Db, null, 64, 1);
            Assert.AreEqual(1, Shop.CargoOf(64));
            Assert.AreEqual(0, Mounted(64));
        }

        [Test]
        public void GiveAndMountMountsIntoAFreeSlot()
        {
            Cheats.GiveAndMount(Db, Stock(70), 64, 1);
            Assert.AreEqual(1, Mounted(64));
            Assert.AreEqual(0, Shop.CargoOf(64));
        }

        [Test]
        public void GiveAndMountSwapsAOnePerShipItem()
        {
            Cheats.GiveAndMount(Db, Stock(70), 55, 1);
            Assert.AreEqual(1, Mounted(55));
            Assert.AreEqual(0, Mounted(59));
            Assert.AreEqual(1, Shop.CargoOf(59), "the old armor in the hold");
        }

        [Test]
        public void GiveAndMountLeavesWhatDoesntFitInTheHold()
        {
            Cheats.GiveAndMount(Db, Stock(70), 0, 1);     // primaries full
            Cheats.GiveAndMount(Db, Stock(70), 122, 7);   // a commodity
            Assert.AreEqual(1, Shop.CargoOf(0));
            Assert.AreEqual(7, Shop.CargoOf(122));
            Assert.AreEqual(7, Session.Equipment.Count);
        }

        // ---- fill the Kaamo Club ----------------------------------------------------------------------------------------

        [Test]
        public void FillKaamoClubOwnsAndStocksIt()
        {
            Cheats.FillKaamoClub(Db);
            Assert.AreEqual(3, Session.KaamoState);
            Assert.AreEqual(Db.Items.Count, Session.KaamoItems.Count);
            foreach (var row in Session.KaamoItems)
                Assert.AreEqual(Db.Item(row.item).TypeId == 1 ? 50 : 1, row.amount, "item " + row.item);
            var ships = Session.KaamoShips.Select(s => s.ship).ToList();
            Assert.AreEqual(58, ships.Count);
            CollectionAssert.AllItemsAreUnique(ships);
            foreach (int s in new[] { 13, 14, 15, 50, 53, 10 }) CollectionAssert.DoesNotContain(ships, s);
            Assert.IsTrue(Session.KaamoShips.All(s => s.mods.Count == 0 && s.equipment.Count == 0), "bare hulls");
        }

        [Test]
        public void FillKaamoClubSkipsStoryItemsAndTopsUp()
        {
            Session.Unsaleable.Add(90);
            Session.KaamoItems.Add(new ItemStack(36, 20));
            Session.KaamoItems.Add(new ItemStack(122, 400));
            Session.KaamoShips.Add(new StoredShip(0, 0, null));
            Cheats.FillKaamoClub(Db);
            Assert.IsNull(Session.KaamoItems.Find(r => r.item == 90), "323: the storage refuses story items");
            Assert.AreEqual(50, Session.KaamoItems.Find(r => r.item == 36).amount, "topped up to 50");
            Assert.AreEqual(400, Session.KaamoItems.Find(r => r.item == 122).amount, "never lowered");
            Assert.AreEqual(1, Session.KaamoItems.Count(r => r.item == 36), "not doubled");
            Assert.AreEqual(58, Session.KaamoShips.Count, "the Betty was already stored");
            int items = Session.KaamoItems.Count, ships = Session.KaamoShips.Count;
            Cheats.FillKaamoClub(Db);
            Assert.AreEqual(items, Session.KaamoItems.Count, "a second fill adds nothing");
            Assert.AreEqual(ships, Session.KaamoShips.Count);
        }

        [Test]
        public void FillKaamoClubLeavesTheFlownShipOut()
        {
            Session.ShipIndex = 44;
            Cheats.FillKaamoClub(Db);
            Assert.IsFalse(KaamoClub.HasShip(44));
            Assert.IsTrue(KaamoClub.HasShip(10));
        }

        [Test]
        public void LockTimeWithoutTheCheat()
        {
            using (new SettingsScope<bool>(() => Cheats.InstantLocks, v => Cheats.InstantLocks = v))
            {
                Cheats.InstantLocks = false;
                Assert.AreEqual(8000, Cheats.LockMs(8000));
                Cheats.InstantLocks = true;
                Assert.AreEqual(0, Cheats.LockMs(8000));
            }
        }

        [Test]
        public void GrantedFlagsToggle()
        {
            try
            {
                Assert.IsTrue(Cheats.Grant("godMode", true));
                Assert.IsFalse(Cheats.Grant("godMode", null), "null toggles");
                Assert.IsTrue(Cheats.Grant("godMode", null));
                Assert.IsFalse(Cheats.Grant("godMode", false));
                Assert.AreEqual(9, Cheats.Flags.Length);
                CollectionAssert.Contains(Cheats.Flags.Select(f => f.key).ToArray(), "freeShopping");
            }
            finally { Cheats.ClearGranted(); }
        }

        // Remake (a modder's suggestion: a mission's hand-in, the Valkyrie's last mission's Khador Drive): Cheats.TakeItem,
        // /take and the missions' Take an item / Bring cargo's hand-in.
        [Test]
        public void TakeItemHandsCargoIn()
        {
            Cheats.GiveItem(5, 10);
            Assert.AreEqual(4, Cheats.TakeItem(5, 4, false, out bool unmounted));
            Assert.IsFalse(unmounted);
            Assert.AreEqual(6, Shop.CargoOf(5));
            Assert.AreEqual(6, Cheats.TakeItem(5, 0, false, out _), "0: all of them");
            Assert.AreEqual(0, Shop.CargoOf(5));
            Assert.AreEqual(0, Cheats.TakeItem(5, 3, false, out _), "none aboard");
        }

        [Test]
        public void TakeItemTakesAMountedKhadorDrive()
        {
            int drive = GalaxyMap.KhadorDriveItem;
            Session.Equipment.RemoveAll(e => e.item == drive);
            Session.Equipment.Add(new ItemStack(drive, 1));
            Session.Unsaleable.Add(drive);
            Assert.AreEqual(0, Cheats.TakeItem(drive, 0, false, out bool unmounted), "the hold alone: the mounted one stays");
            Assert.IsFalse(unmounted);
            Assert.IsTrue(Session.Equipment.Exists(e => e.item == drive));
            Assert.AreEqual(1, Cheats.TakeItem(drive, 0, true, out unmounted));
            Assert.IsTrue(unmounted);
            Assert.IsFalse(Session.Equipment.Exists(e => e.item == drive));
            Assert.IsFalse(Session.Unsaleable.Contains(drive), "the story's flag goes with the last one");
            // A mod hull's built-in item stays.
            Session.Equipment.Add(new ItemStack(drive, 1) { builtIn = true });
            Assert.AreEqual(0, Cheats.TakeItem(drive, 0, true, out unmounted));
            Assert.IsFalse(unmounted);
        }

        [Test]
        public void TakeItemTakesSecondaryAmmoByTheUnit()
        {
            Session.Equipment.RemoveAll(e => e.item == 37);
            Session.Equipment.Add(new ItemStack(37, 30));
            Cheats.GiveItem(37, 5);
            Assert.AreEqual(12, Cheats.TakeItem(37, 12, true, out bool unmounted), "the hold's 5 first, then 7 of the mounted 30");
            Assert.IsTrue(unmounted);
            Assert.AreEqual(0, Shop.CargoOf(37));
            Assert.AreEqual(23, Session.Equipment.Find(e => e.item == 37).amount);
        }
    }
}
