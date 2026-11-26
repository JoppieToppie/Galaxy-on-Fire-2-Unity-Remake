// HangarMountTests.cs
// Feature: CLAUDE.md "Shop" > Mounting (first free slot of the item's type, one-per-ship categories swap (287), secondaries
// move with their ammo, bought missiles join the mounted stack), the docking fit (Hangar.FitToSlots: the overflow to the
// hold, story items keep their place), the cloak bay on ships 44 / 49 (Settings.CloakBay), the equipment mod (+1 slot).
// Expected: Reference/research/shop.md 6 (HangarWindow::mountItem / demountItem / autoEquipSecondaryWeapons, the
// multi-install table) and CLAUDE.md "Combat equipment" > Cloak (the bay). Slot counts from ships.json: Phantom (10)
// 2 / 1 / 0 / 9, Betty (0) 1 / 1 / 0 / 3, Specter (44) 4 / 2 / 0 / 16. The new game's equipment (shop.md 8): 2, 2 primary,
// 36 x 6 secondary, 54 shield, 59 armor, 82 scanner, 73 booster.

using System.Collections.Generic;
using System.Linq;
using GoF2Remake.Data;
using GoF2Remake.Flight;
using NUnit.Framework;

namespace GoF2Remake.Tests
{
    public class HangarMountTests : EconomyTest
    {
        Hangar Dis() => new Hangar(Db, Stock(70, 0, 5, 55, 5, 122, 5));

        [Test]
        public void SlotCountsAreTheShips()
        {
            var h = Dis();
            Assert.AreEqual(2, h.SlotCount(0));
            Assert.AreEqual(1, h.SlotCount(1));
            Assert.AreEqual(0, h.SlotCount(2));
            Assert.AreEqual(9, h.SlotCount(3));
            Assert.AreEqual(0, h.SlotCount(4), "commodities have no slot");
            Assert.AreEqual(new[] { 0, 1 }, h.MountedOfType(0).ToArray());
            Assert.AreEqual(new[] { 3, 4, 5, 6 }, h.MountedOfType(3).ToArray());
        }

        [Test]
        public void EquipmentModAddsASlotPerLevel()
        {
            Session.ShipMods.Add(2);
            Assert.AreEqual(10, Dis().SlotCount(3));
            Session.ShipMods.Add(2);
            Assert.AreEqual(11, Dis().SlotCount(3));
        }

        [Test]
        public void MountTakesOneUnitIntoAFreeSlot()
        {
            Session.Cargo.Add(new ItemStack(64, 2));   // Autopacker 2: compression, multi-installable
            var h = Dis();
            Assert.AreEqual(Hangar.Result.Ok, h.CanMount(64, out int swap));
            Assert.AreEqual(-1, swap);
            Assert.IsTrue(h.Mount(64));
            Assert.AreEqual(1, h.CargoOf(64));
            Assert.AreEqual(1, Mounted(64));
            Assert.IsTrue(h.Mount(64), "a second one: compression stacks");
            Assert.AreEqual(2, Mounted(64));
            Assert.IsEmpty(Session.Cargo);
        }

        [Test]
        public void DuplicateSecondaryMountsAreDetectedWithoutRelaxingOtherTypes()
        {
            var mounted = new List<ItemStack> { new ItemStack(36, 4) };   // Edo is a secondary
            Assert.IsTrue(Hangar.IsDuplicateSecondaryMount(Db, 36, mounted));
            Assert.IsFalse(Hangar.DuplicateSecondaryMountAllowed(Db, 36, mounted, false), "vanilla default is unchanged");
            Assert.IsTrue(Hangar.DuplicateSecondaryMountAllowed(Db, 36, mounted, true), "an active mod enables the exception");
            Assert.IsFalse(Hangar.IsDuplicateSecondaryMount(Db, 2, mounted), "primary weapons are unaffected");
            Assert.IsTrue(Hangar.IsDuplicateSecondaryMount(Db, 36,
                new List<ItemStack> { new ItemStack(36, 1) { builtIn = true } }), "an integrated secondary is still a hardpoint");
        }

        [Test]
        public void ExplicitSecondaryDistributionConservesRoundsAndHonorsSelectedSlots()
        {
            string dir = System.IO.Path.Combine(Sandbox, "secondary-opt-in");
            System.IO.Directory.CreateDirectory(dir);
            System.IO.File.WriteAllText(System.IO.Path.Combine(dir, "mod.json"),
                "{\"id\":\"secondary_opt_in\",\"multipleSecondaryMounts\":true}");
            GoF2Remake.Modding.ModManager.SetBuilderProject(dir);
            try
            {
                Session.ShipIndex = 44; // two secondary slots
                Session.Equipment = new List<ItemStack> { new ItemStack(36, 6) { slot = 1 } };
                Session.Cargo = new List<ItemStack> { new ItemStack(36, 5) };
                var h = Dis();
                Assert.IsTrue(GoF2Remake.Modding.ModContent.MultipleSecondaryMountsEnabled);
                CollectionAssert.AreEqual(new[] { 1, 2 }, h.SecondaryDistributionSlots(36));
                Assert.IsEmpty(h.AutoEquipSecondaries(), "selection must not consume cargo meant for another launcher");
                Assert.IsFalse(h.DistributeSecondaries(36, new[] { 3 }), "out-of-range mount");
                Assert.IsTrue(h.TryPreviewSecondaryDistribution(36, new[] { 2, 1, 1 }, out var preview, out long cargo, out long mounted));
                Assert.AreEqual(5, cargo);
                Assert.AreEqual(6, mounted);
                Assert.AreEqual(6, preview[1]);
                Assert.AreEqual(5, preview[2]);
                Assert.AreEqual(6, Session.Equipment.Single(e => e.slot == 1).amount, "preview does not mutate fitting");
                Assert.AreEqual(5, h.CargoOf(36), "preview does not consume cargo");
                Assert.AreEqual(5, h.CargoOf(36));
                Assert.IsTrue(h.DistributeSecondaries(36, new[] { 2 }));
                Assert.AreEqual(6, Session.Equipment.Single(e => e.slot == 1).amount, "unselected stack remains intact");
                Assert.AreEqual(5, Session.Equipment.Single(e => e.slot == 2).amount);
                Assert.AreEqual(0, h.CargoOf(36));
                Shop.AddToCargo(36, 2);
                Assert.IsEmpty(h.AutoEquipSecondaries(), "multiple mounted copies still permit manual allocation");
                Assert.IsTrue(h.DistributeSecondaries(36, new[] { 2, 1 }));
                Assert.AreEqual(7, Session.Equipment.Single(e => e.slot == 1).amount);
                Assert.AreEqual(6, Session.Equipment.Single(e => e.slot == 2).amount);
                Assert.AreEqual(13, AmountMounted(36));
                Assert.AreEqual(0, h.CargoOf(36));
                Session.Equipment.Single(e => e.slot == 2).item = 37;
                Shop.AddToCargo(36, 3);
                Assert.IsFalse(h.DistributeSecondaries(36, new[] { 1, 2 }), "never replace another weapon");
                Assert.AreEqual(3, h.CargoOf(36));
            }
            finally { GoF2Remake.Modding.ModManager.SetBuilderProject(null); }
        }

        [Test]
        public void MultipleSecondaryMountsShareOneAmmoPool()
        {
            var first = new ItemStack(36, 1);
            var second = new ItemStack(36, 2);
            var pool = new SecondaryAmmoPool();
            pool.AddStack(first);
            pool.AddStack(second);
            Assert.AreEqual(3, pool.Amount);
            Assert.IsTrue(pool.HasAmmo);
            Assert.AreEqual(1, pool.ConsumeOne());
            Assert.AreEqual(2, pool.Amount);
            Assert.AreEqual(0, first.amount, "the pool exhausts the first saved stack");
            Assert.AreEqual(2, second.amount, "then consumes from the next stack");
            Assert.AreEqual(4, pool.Add(4));
            Assert.AreEqual(6, pool.Amount);
            Assert.AreEqual(2, first.amount, "restock is shared rather than being assigned wholly to the first mount");
            Assert.AreEqual(4, second.amount, "both saved mount stacks retain rounds if either launcher is demounted");
            Assert.Greater(Hangar.UnitsOf(Db, first), 0, "the first launcher can be demounted with its own ammunition");
            Assert.Greater(Hangar.UnitsOf(Db, second), 0, "the second launcher can be demounted with its own ammunition");
        }

        [TestCase(0)]
        [TestCase(1)]
        public void SharedRestockSurvivesDemountingEitherLauncher(int demountIndex)
        {
            Session.ShipIndex = 44;   // Specter: two secondary hardpoints
            Session.Cargo.RemoveAll(x => x.item == 36);
            Session.Equipment = new List<ItemStack>();
            Hangar.SyncBuiltIn(Db);
            var first = new ItemStack(36, 1);
            var second = new ItemStack(36, 1);
            Session.Equipment.Add(first);
            Session.Equipment.Add(second);
            var pool = new SecondaryAmmoPool();
            pool.AddStack(first);
            pool.AddStack(second);
            Assert.AreEqual(10, pool.Add(10));
            Assert.AreEqual(6, first.amount);
            Assert.AreEqual(6, second.amount);

            var removed = demountIndex == 0 ? first : second;
            var surviving = demountIndex == 0 ? second : first;
            Dis().Demount(Session.Equipment.IndexOf(removed));

            Assert.AreEqual(6, Shop.CargoOf(36), "the removed launcher takes only its own saved ammo");
            Assert.IsFalse(Session.Equipment.Contains(removed));
            Assert.IsTrue(Session.Equipment.Contains(surviving));
            Assert.AreEqual(6, surviving.amount, "the surviving launcher retains its ammunition");
        }

        [Test]
        public void BuiltInAndFittedDuplicateSecondariesCanShareAndRestock()
        {
            var builtIn = new ItemStack(36, 1) { builtIn = true };
            var fitted = new ItemStack(36, 2);
            var pool = new SecondaryAmmoPool();
            pool.AddStack(builtIn);
            pool.AddStack(fitted);

            Assert.AreEqual(3, pool.Amount);
            Assert.AreEqual(6, pool.Add(6));
            Assert.AreEqual(4, builtIn.amount);
            Assert.AreEqual(5, fitted.amount);
            Assert.AreEqual(9, pool.Amount);
        }

        [Test]
        public void SeparatePoolsPreserveVanillaPerMountAmmunition()
        {
            var first = new ItemStack(36, 2);
            var second = new ItemStack(36, 3);
            var firstMount = new SecondaryAmmoPool();
            var secondMount = new SecondaryAmmoPool();
            firstMount.AddStack(first);
            secondMount.AddStack(second);

            Assert.AreEqual(2, firstMount.Amount);
            Assert.AreEqual(3, secondMount.Amount);
            Assert.AreEqual(4, firstMount.Add(2));
            Assert.AreEqual(4, first.amount);
            Assert.AreEqual(3, second.amount, "a vanilla independent mount is not changed by the other pool");
        }

        [Test]
        public void FullSlotsRefuse()
        {
            Session.Cargo.Add(new ItemStack(0, 1));   // a third laser on a 2-primary ship
            var h = Dis();
            Assert.AreEqual(Hangar.Result.NoFreeSlot, h.CanMount(0, out _));
            Assert.IsFalse(h.Mount(0));
            Assert.AreEqual(1, h.CargoOf(0));
        }

        [Test]
        public void CommoditiesArentMountable()
        {
            Session.Cargo.Add(new ItemStack(122, 1));
            Assert.AreEqual(Hangar.Result.NotMountable, Dis().CanMount(122, out _));
            Assert.AreEqual(Hangar.Result.NothingToSell, Dis().CanMount(55, out _), "nothing of it in the hold");
        }

        [Test]
        public void AModCanRefuseBaseEquipmentForAMount()
        {
            var previous = Hangar.MountRefusal;
            try
            {
                Hangar.MountRefusal = (ship, item) => item == 84 ? "Capital ships cannot use base equipment." : null;
                Session.Cargo.Add(new ItemStack(84, 1));   // Hiroto Ultrascan
                var h = Dis();
                Assert.AreEqual(Hangar.Result.NotMountable, h.CanMount(84, out _));
                Assert.IsFalse(h.Mount(84));
                Assert.IsFalse(h.Swap(4, 84), "the swap path must obey the same rule");
                Assert.AreEqual(1, h.CargoOf(84), "a refused item stays in the hold");
            }
            finally { Hangar.MountRefusal = previous; }
        }

        [Test]
        public void ShipSyncReturnsNewlyForbiddenEquipmentToTheHold()
        {
            var previous = Hangar.MountRefusal;
            try
            {
                Hangar.MountRefusal = (ship, item) => item == 84 ? "Capital ships cannot use base equipment." : null;
                Session.Equipment.Add(new ItemStack(84, 1));   // already fitted before the class rule applies
                Assert.IsTrue(Hangar.SyncBuiltIn(Db));
                Assert.AreEqual(0, Mounted(84));
                Assert.AreEqual(1, Shop.CargoOf(84), "do not destroy equipment when a ship changes");
            }
            finally { Hangar.MountRefusal = previous; }
        }

        [Test]
        public void OnePerShipCategorySwaps()
        {
            // 287: the E2 Exoclad armor (55) for the mounted T'yol (59, equipment index 4)
            Session.Cargo.Add(new ItemStack(55, 1));
            var h = Dis();
            Assert.AreEqual(Hangar.Result.Swap, h.CanMount(55, out int swapWith));
            Assert.AreEqual(4, swapWith);
            Assert.IsFalse(h.Mount(55), "Mount only takes a free slot");
            Assert.IsTrue(h.Swap(swapWith, 55));
            Assert.AreEqual(1, Mounted(55));
            Assert.AreEqual(0, Mounted(59));
            Assert.AreEqual(1, h.CargoOf(59));
            Assert.AreEqual(0, h.CargoOf(55));
        }

        [Test]
        public void TheKhadorSlipstreamAsksToReplaceTheKhadorDrive()
        {
            int slipstream = GoF2Remake.Modding.ModContent.ItemIndexOf("gof2:khador_slipstream");
            Assert.GreaterOrEqual(slipstream, 0, "the base game's Khador Slipstream");
            Session.Equipment.Add(new ItemStack(GalaxyMap.KhadorDriveItem, 1));
            Session.Cargo.Add(new ItemStack(slipstream, 1));
            var h = Dis();
            int khador = Session.Equipment.FindIndex(e => e.item == GalaxyMap.KhadorDriveItem);
            Assert.AreEqual(Hangar.Result.Swap, h.CanMount(slipstream, out int swapWith), "287, not a second drive");
            Assert.AreEqual(khador, swapWith);
            Assert.IsTrue(h.Swap(swapWith, slipstream));
            Assert.AreEqual(1, Mounted(slipstream));
            Assert.AreEqual(0, Mounted(GalaxyMap.KhadorDriveItem));
            Assert.AreEqual(1, h.CargoOf(GalaxyMap.KhadorDriveItem), "the Khador Drive goes to the hold");
            Assert.AreEqual(Hangar.Result.Swap, h.CanMount(GalaxyMap.KhadorDriveItem, out swapWith), "and back the other way");
        }

        [Test]
        public void SwapCannotReplaceAnUnrelatedMount()
        {
            Session.Cargo.Add(new ItemStack(55, 1));
            var h = Dis();
            Assert.AreEqual(Hangar.Result.Swap, h.CanMount(55, out int correct));
            Assert.AreEqual(4, correct);
            Assert.IsFalse(h.Swap(3, 55));
            Assert.AreEqual(1, h.CargoOf(55));
            Assert.AreEqual(1, Mounted(54));
        }

        [Test]
        public void AutoReplenishmentAndDockingRespectClassRefusal()
        {
            var previous = Hangar.MountRefusal;
            try
            {
                Hangar.MountRefusal = (ship, item) => item == 36 ? "Restricted" : null;
                Session.Cargo.Add(new ItemStack(36, 5));
                Assert.IsEmpty(Dis().AutoEquipSecondaries());
                Assert.AreEqual(5, Shop.CargoOf(36));
                Assert.AreEqual(1, Hangar.FitToSlots(Db));
                Assert.AreEqual(0, Mounted(36));
                Assert.AreEqual(11, Shop.CargoOf(36));
            }
            finally { Hangar.MountRefusal = previous; }
        }

        [Test]
        public void BlueprintAmmoRemainsAvailableForOptInDistribution()
        {
            string dir = System.IO.Path.Combine(Sandbox, "secondary-blueprint-opt-in");
            System.IO.Directory.CreateDirectory(dir);
            System.IO.File.WriteAllText(System.IO.Path.Combine(dir, "mod.json"),
                "{\"id\":\"secondary_blueprint_opt_in\",\"multipleSecondaryMounts\":true}");
            GoF2Remake.Modding.ModManager.SetBuilderProject(dir);
            try
            {
                Session.ShipIndex = 44;
                Session.Equipment = new List<ItemStack> { new ItemStack(37, 2) { slot = 1 } };
                Session.Cargo = new List<ItemStack>();
                Assert.IsTrue(Blueprints.Produce(Db, 37, 70));
                Assert.AreEqual(10, Shop.CargoOf(37));
                Assert.AreEqual(2, AmountMounted(37));
                Assert.IsTrue(Dis().DistributeSecondaries(37, new[] { 1, 2 }));
                Assert.AreEqual(12, AmountMounted(37));
                Assert.AreEqual(0, Shop.CargoOf(37));
            }
            finally { GoF2Remake.Modding.ModManager.SetBuilderProject(null); }
        }

        [Test]
        public void SwapRefusesBadInput()
        {
            var h = Dis();
            Assert.IsFalse(h.Swap(4, 55), "nothing in the hold");
            Session.Cargo.Add(new ItemStack(55, 1));
            Assert.IsFalse(h.Swap(99, 55));
            Assert.IsFalse(h.Swap(-1, 55));
            Assert.AreEqual(7, Session.Equipment.Count);
        }

        [Test]
        public void SecondariesMoveWithTheirAmmo()
        {
            var h = Dis();
            h.Demount(2);   // Edo x 6
            Assert.AreEqual(6, h.CargoOf(36));
            Assert.AreEqual(0, Mounted(36));
            Session.Cargo.Add(new ItemStack(37, 10));
            Assert.IsTrue(h.Mount(37));
            Assert.AreEqual(10, AmountMounted(37), "the whole stack is the ammo");
            Assert.AreEqual(0, h.CargoOf(37));
            Assert.AreEqual(Hangar.Result.NoFreeSlot, h.CanMount(36, out _));
        }

        [Test]
        public void MountedOfCountsAmmoForSecondaries()
        {
            var h = Dis();
            Assert.AreEqual(6, h.MountedOf(36));
            Assert.AreEqual(2, h.MountedOf(2));
            Assert.AreEqual(1, h.MountedOf(54));
            Assert.IsTrue(h.IsMounted(82));
            Assert.IsFalse(h.IsMounted(55));
        }

        [Test]
        public void DemountPutsOneUnitBack()
        {
            var h = Dis();
            h.Demount(3);   // the shield
            Assert.AreEqual(1, h.CargoOf(54));
            Assert.AreEqual(6, Session.Equipment.Count);
            h.Demount(42);
            h.Demount(-1);
            Assert.AreEqual(6, Session.Equipment.Count, "a bad index does nothing");
        }

        [Test]
        public void AnEmptySecondaryIsNothingOffTheShip()
        {
            // A player's video: an AMR Extinctor fired empty, demounted into the hold as 1 t, mounted again, over and over.
            Session.Equipment[2].amount = 0;   // the 6 missiles, all fired
            Assert.AreEqual(0, Hangar.UnitsOf(Db, Session.Equipment[2]));
            Assert.AreEqual(1, Hangar.UnitsOf(Db, Session.Equipment[3]), "other items count one");
            var h = Dis();
            h.Demount(2);
            Assert.AreEqual(0, h.CargoOf(36), "no round from an empty launcher");
            KaamoClub.AddToStorage(new[] { new ItemStack(36, 0) });
            Assert.IsFalse(Session.KaamoItems.Exists(s => s.item == 36), "nor in the club's storage");
        }

        [Test]
        public void BoughtMissilesJoinTheMountedStack()
        {
            Session.Cargo.Add(new ItemStack(36, 5));
            Session.Cargo.Add(new ItemStack(37, 4));   // not mounted: stays in the hold
            Session.Cargo.Add(new ItemStack(122, 2));
            var h = Dis();
            var moved = h.AutoEquipSecondaries();
            Assert.AreEqual(new[] { 36 }, moved.ToArray());
            Assert.AreEqual(11, AmountMounted(36));
            Assert.AreEqual(0, h.CargoOf(36));
            Assert.AreEqual(4, h.CargoOf(37));
            Assert.AreEqual(2, h.CargoOf(122));
        }

        // ---- docking fit -----------------------------------------------------------------------------------------

        [Test]
        public void FitToSlotsMovesTheOverflowToTheHold()
        {
            // the start loadout on a Betty (1 / 1 / 0 / 3): the second laser and the fourth equipment (the booster) go
            Session.ShipIndex = 0;
            Assert.AreEqual(2, Hangar.FitToSlots(Db));
            CollectionAssert.AreEqual(new[] { 2, 36, 54, 59, 82 }, Session.Equipment.Select(e => e.item).ToArray());
            Assert.AreEqual(1, Shop.CargoOf(2));
            Assert.AreEqual(1, Shop.CargoOf(73));
            Assert.AreEqual(0, Hangar.FitToSlots(Db), "fits now");
        }

        [Test]
        public void FitToSlotsKeepsStoryItemsInPlace()
        {
            // an unsaleable item stays mounted and takes its slot first: the booster (73) stays, the scanner (82) goes
            Session.ShipIndex = 0;
            Session.Unsaleable.Add(73);
            Assert.AreEqual(2, Hangar.FitToSlots(Db));
            CollectionAssert.AreEqual(new[] { 2, 36, 54, 59, 73 }, Session.Equipment.Select(e => e.item).ToArray());
            Assert.AreEqual(1, Shop.CargoOf(82));
            Assert.AreEqual(1, Shop.CargoOf(2));
            Assert.AreEqual(0, Shop.CargoOf(73));
        }

        [Test]
        public void FitToSlotsMovesSecondariesWithTheirAmmo()
        {
            Session.ShipIndex = 38;   // Typhon: 4 primaries, no secondary slot
            Assert.AreEqual(1, Hangar.FitToSlots(Db));
            Assert.AreEqual(6, Shop.CargoOf(36));
        }

        [Test]
        public void FitToSlotsLeavesAFittingLoadoutAlone()
        {
            var before = Session.Equipment;
            Assert.AreEqual(0, Hangar.FitToSlots(Db));
            Assert.AreSame(before, Session.Equipment);
        }

        // ---- the cloak bay (remake option) --------------------------------------------------------------------------

        [Test]
        public void CloakBayOnlyOnTheSpecterAndScimitar()
        {
            using (new SettingsScope<bool>(() => Settings.CloakBay, v => Settings.CloakBay = v))
            {
                Settings.CloakBay = true;
                Assert.IsTrue(Hangar.HasCloakBay(44));
                Assert.IsTrue(Hangar.HasCloakBay(49));
                Assert.IsFalse(Hangar.HasCloakBay(10));
                Settings.CloakBay = false;
                Assert.IsFalse(Hangar.HasCloakBay(44));
            }
        }

        [Test]
        public void MountedCloakAddsTheBaySlot()
        {
            using (new SettingsScope<bool>(() => Settings.CloakBay, v => Settings.CloakBay = v))
            {
                Settings.CloakBay = true;
                Session.ShipIndex = 44;
                Assert.AreEqual(16, Dis().SlotCount(3));
                Assert.IsTrue(Hangar.BayTakes(Db, 44, 94, Session.Equipment));
                Assert.IsFalse(Hangar.BayTakes(Db, 44, 54, Session.Equipment), "only a cloak");
                Session.Equipment.Add(new ItemStack(94, 1));
                Assert.AreEqual(1, Hangar.BaySlot(Db, 44, Session.Equipment));
                Assert.AreEqual(17, Dis().SlotCount(3));
                Assert.IsFalse(Hangar.BayTakes(Db, 44, 96, Session.Equipment), "the bay is taken");
                Settings.CloakBay = false;
                Assert.AreEqual(16, Dis().SlotCount(3));
                Assert.AreEqual(0, Hangar.BaySlot(Db, 44, Session.Equipment));
            }
        }

        [Test]
        public void CloakGoesIntoTheBayWhenTheSlotsAreFull()
        {
            using (new SettingsScope<bool>(() => Settings.CloakBay, v => Settings.CloakBay = v))
            {
                Session.ShipIndex = 44;
                Session.Equipment = Enumerable.Range(0, 16).Select(_ => new ItemStack(64, 1)).ToList();
                Session.Cargo.Add(new ItemStack(94, 1));
                Settings.CloakBay = true;
                Assert.AreEqual(Hangar.Result.Ok, Dis().CanMount(94, out _));
                Settings.CloakBay = false;
                Assert.AreEqual(Hangar.Result.NoFreeSlot, Dis().CanMount(94, out _), "the original: a slot like any equipment");
            }
        }

        [Test]
        public void FitToSlotsCountsTheBay()
        {
            using (new SettingsScope<bool>(() => Settings.CloakBay, v => Settings.CloakBay = v))
            {
                Session.ShipIndex = 44;
                Session.Equipment = Enumerable.Range(0, 16).Select(_ => new ItemStack(64, 1)).ToList();
                Session.Equipment.Add(new ItemStack(94, 1));
                Settings.CloakBay = true;
                Assert.AreEqual(0, Hangar.FitToSlots(Db));
                Settings.CloakBay = false;
                Assert.AreEqual(1, Hangar.FitToSlots(Db), "turned off: what no longer fits goes to the hold");
                Assert.AreEqual(1, Shop.CargoOf(94));
            }
        }

        // ---- "Mount it now?" after a blueprint run (remake, a player's suggestion) ------------------------------------
        // HangarWindow.Finish asks only when Hangar.OffersMountAfterBuild: the product went to the hold at its production
        // station and has a free slot (no swap, no commodity, not merged into a mounted stack).

        [Test]
        public void AFinishedProductWithAFreeSlotIsOffered()
        {
            Assert.IsTrue(Blueprints.Produce(Db, 90, 70));   // Gunant's Drill: no drill mounted, 5 equipment slots free
            var h = Dis();
            Assert.IsTrue(h.OffersMountAfterBuild(90));
            Assert.IsTrue(h.Mount(90));
            Assert.AreEqual(1, Mounted(90));
            Assert.AreEqual(0, h.CargoOf(90));
        }

        [Test]
        public void NoOfferWithoutAFreeSlot()
        {
            Assert.IsTrue(Blueprints.Produce(Db, 11, 70));   // a primary: both of the Phantom's primary slots are taken
            Assert.IsFalse(Dis().OffersMountAfterBuild(11));
            Assert.AreEqual(1, Shop.CargoOf(11), "it stays in the hold");
        }

        [Test]
        public void NoOfferWhenItWouldSwap()
        {
            Assert.IsTrue(Blueprints.Produce(Db, 54, 70));   // a shield, and the Phantom already has one (287's swap)
            Assert.AreEqual(Hangar.Result.Swap, Dis().CanMount(54, out _));
            Assert.IsFalse(Dis().OffersMountAfterBuild(54));
        }

        [Test]
        public void NoOfferForASecondaryThatJoinedItsMountedStack()
        {
            Session.Equipment.RemoveAll(e => e.item == 36);
            Session.Equipment.Add(new ItemStack(37, 2));
            Assert.IsTrue(Blueprints.Produce(Db, 37, 70));
            Assert.AreEqual(12, AmountMounted(37));
            Assert.IsFalse(Dis().OffersMountAfterBuild(37), "nothing of it left in the hold");
        }
    }
}
