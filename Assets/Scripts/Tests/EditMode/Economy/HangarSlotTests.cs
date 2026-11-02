// HangarSlotTests.cs
// Feature: CLAUDE.md "Shop" > Slot positions: a mounted item keeps its slot (ItemStack.slot, Hangar.SlotPositions /
// PinSlots), a demount leaves its slot empty and a mount fills the type's first empty one, each gun on the mount of its
// slot (Hangar.MountOrder, ModHardpoints.WeaponItems), the remake's MOVE / drag (Hangar.MoveToSlot, NextSlot), the docking
// fit packing what sits past a smaller hull's slots, and a new hull taking the gear in its slots' order.
// Expected: Ship::removeEquipment 0x1a3e4c (the slot set to null), Ship::addEquipment 0x1a4088 (the type's first null slot),
// Ship::getSlotPos 0x1a3f8c and Level::createPlayer (Gun::setOffset(mounts[getSlotPos])). The Dace (4) has 4 primary slots,
// the Badger (3) 2; items 0, 1, 2, 5 are primaries.

using System.Linq;
using GoF2Remake.Data;
using NUnit.Framework;
using UnityEngine;

namespace GoF2Remake.Tests
{
    public class HangarSlotTests : EconomyTest
    {
        Hangar Dace(params int[] primaries)
        {
            Session.ShipIndex = 4;
            Session.Equipment = primaries.Select(i => new ItemStack(i, 1)).ToList();
            return new Hangar(Db, Stock(70, 5, 5));
        }

        int[] Items(int[] slots) => slots.Select(e => e < 0 ? -1 : Session.Equipment[e].item).ToArray();

        [Test]
        public void DemountLeavesTheSlotEmpty()
        {
            var h = Dace(0, 1, 2);
            h.Demount(1);
            CollectionAssert.AreEqual(new[] { 0, -1, 2, -1 }, Items(h.SlotsOfType(0)), "the others stay where they are");
            CollectionAssert.AreEqual(new[] { 0, -1, 1 }, Hangar.MountOrder(Db, Session.Equipment, 0), "the third gun stays on the third mount");
            Assert.AreEqual(new[] { 0, 2 }, h.MountedOfType(0).Select(e => Session.Equipment[e].item).ToArray());
        }

        [Test]
        public void MountFillsTheFirstEmptySlot()
        {
            var h = Dace(0, 1, 2);
            h.Demount(1);
            Session.Cargo.Add(new ItemStack(5, 1));
            Assert.IsTrue(h.Mount(5));
            CollectionAssert.AreEqual(new[] { 0, 5, 2, -1 }, Items(h.SlotsOfType(0)));
            Session.Cargo.Add(new ItemStack(1, 1));
            Assert.IsTrue(h.Mount(1));
            CollectionAssert.AreEqual(new[] { 0, 5, 2, 1 }, Items(h.SlotsOfType(0)));
        }

        [Test]
        public void AnItemUsedUpInFlightLeavesItsSlotEmpty()
        {
            var h = Dace(0, 1, 2);
            Hangar.SyncBuiltIn(Db);   // as a level starts: the slots pinned
            Session.Equipment.RemoveAt(0);
            CollectionAssert.AreEqual(new[] { -1, 1, 2, -1 }, Items(h.SlotsOfType(0)));
        }

        [Test]
        public void MoveToSlotSwapsOrMovesIntoAnEmptyOne()
        {
            var h = Dace(0, 1);
            Assert.IsTrue(h.MoveToSlot(0, 3), "into an empty slot");
            CollectionAssert.AreEqual(new[] { -1, 1, -1, 0 }, Items(h.SlotsOfType(0)));
            Assert.IsTrue(h.MoveToSlot(1, 3), "onto a mounted one: they change places");
            CollectionAssert.AreEqual(new[] { -1, 0, -1, 1 }, Items(h.SlotsOfType(0)));
            Assert.IsFalse(h.MoveToSlot(1, 3), "already there");
            Assert.IsFalse(h.MoveToSlot(1, 4), "past the ship's slots");
            CollectionAssert.AreEqual(new[] { 0, 1 }, Session.Equipment.Select(e => e.item).ToArray(), "nothing mounted or demounted");
        }

        [Test]
        public void NextSlotWrapsAndNeedsTwoSlots()
        {
            var h = Dace(0, 1);
            Assert.AreEqual(1, h.NextSlot(0));
            h.MoveToSlot(0, 3);
            Assert.AreEqual(0, h.NextSlot(0), "the last slot wraps to the first");
            Session.ShipIndex = 0;   // the Betty: one primary slot
            Session.Equipment = new System.Collections.Generic.List<ItemStack> { new ItemStack(0, 1) };
            Assert.AreEqual(-1, new Hangar(Db, Stock(70)).NextSlot(0));
        }

        [Test]
        public void StacksWithoutASlotTakeTheFirstFreeOneInListOrder()
        {
            // an old save (slot 0 everywhere) keeps its order; a slot another stack took first counts as none
            var gear = new[] { new ItemStack(0, 1), new ItemStack(1, 1) { slot = 1 }, new ItemStack(2, 1) { slot = 1 } };
            CollectionAssert.AreEqual(new[] { 1, 0, 2 }, Hangar.SlotPositions(Db, gear));
            Hangar.PinSlots(Db, gear);
            CollectionAssert.AreEqual(new[] { 2, 1, 3 }, gear.Select(e => e.slot).ToArray());
            var old = JsonUtility.FromJson<ItemStack>("{\"item\":5,\"amount\":1}");
            Assert.AreEqual(0, old.slot, "a stack saved before slots");
            Assert.AreEqual(3, JsonUtility.FromJson<ItemStack>(JsonUtility.ToJson(new ItemStack(5, 1) { slot = 3 })).slot);
        }

        [Test]
        public void FitToSlotsPacksWhatSitsPastASmallerHull()
        {
            var h = Dace(0, 1);
            h.MoveToSlot(1, 3);   // slots 1 and 4
            Session.ShipIndex = 3;   // the Badger: 2 primaries (a mod's lower count, a debug swap)
            Assert.AreEqual(0, Hangar.FitToSlots(Db), "both fit");
            CollectionAssert.AreEqual(new[] { 1, 2 }, Session.Equipment.Select(e => e.slot).ToArray());
        }

        [Test]
        public void FitToSlotsSendsTheLastSlotsToTheHold()
        {
            var h = Dace(0, 1, 2);
            h.MoveToSlot(0, 3);   // 0 in slot 4: it goes first
            Session.ShipIndex = 3;
            Assert.AreEqual(1, Hangar.FitToSlots(Db));
            CollectionAssert.AreEqual(new[] { 1, 2 }, Session.Equipment.Select(e => e.item).ToArray());
            Assert.AreEqual(1, Shop.CargoOf(0));
        }

        [Test]
        public void MountOrderPutsBuiltInsFirstAndKeepsTheGaps()
        {
            // a remake mod's built-in gun takes the first mount, the slots follow (each on the mount of its slot)
            var gear = new[] { new ItemStack(1, 1) { builtIn = true }, new ItemStack(0, 1) { slot = 3 }, new ItemStack(36, 6) { slot = 1 }, new ItemStack(2, 1) { slot = 1 } };
            CollectionAssert.AreEqual(new[] { 0, 3, -1, 1 }, Hangar.MountOrder(Db, gear, 0));
            CollectionAssert.AreEqual(new[] { 2 }, Hangar.MountOrder(Db, gear, 1), "the secondaries' slots are their own");
        }

        [Test]
        public void ANewHullKeepsTheSlotsOrderPacked()
        {
            var h = Dace(0, 1, 2);
            h.MoveToSlot(0, 3);   // 1, 2, -, 0
            Session.Cargo.Clear();
            Session.Credits = 10_000_000;
            h.Stock.ships.Add(3);
            Assert.IsTrue(h.BuyShip(3), "the Badger");
            CollectionAssert.AreEqual(new[] { 1, 2 }, Session.Equipment.Where(e => Db.Item(e.item).TypeId == 0).OrderBy(e => e.slot).Select(e => e.item).ToArray(),
                                      "the first two slots' guns stay, the last one's goes to the hold");
        }
    }
}
