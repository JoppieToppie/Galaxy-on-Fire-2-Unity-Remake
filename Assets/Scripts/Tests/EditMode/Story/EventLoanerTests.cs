// EventLoanerTests.cs
// Feature: docs/notes/mods.md "Event loaners" (remake, a modder's request 2026-11-02): an event graph's "lend" step lends a
// ship like the Valkyrie's S'Kanarr (Story.LendForEvent): the own ship parked with its equipment, cargo, mods and damage
// (Session.ParkedShip, which also locks the Hangar), the loaner a fresh hull with an empty hold, or with "keep" carrying the
// player's equipment and cargo; Story.ReturnEventLoaner gives the own ship back (only the lending graph's loaner, never the
// story's), and a save loaded without the lending graph under way gives it back at once (Story.CheckEventLoaner).
// Expected values: the story's own loaner rules (Story.ParkOwnShip / LendShip / RestoreOwnShip) and the feature's notes.

using System.Collections.Generic;
using System.Linq;
using GoF2Remake.Data;
using NUnit.Framework;

namespace GoF2Remake.Tests
{
    [Unity.Scripting.LifecycleManagement.NoAutoStaticsCleanup]
    public class EventLoanerTests : StoryAreaTestBase
    {
        [SetUp]
        public void OwnShip()
        {
            Session.ParkedShip = null;
            Session.ShipIndex = 10;
            Session.Equipment = new List<ItemStack> { new ItemStack(2, 1), new ItemStack(54, 1) };
            Session.Cargo = new List<ItemStack> { new ItemStack(122, 4) };
            Session.PlayerHull = 37;
            Session.GraphQuests.Clear();
        }

        [TearDown]
        public void NoLoaner() => Session.ParkedShip = null;

        [Test]
        public void AFreshLoanerAndTheOwnShipBack()
        {
            Session.FreePlay = true;
            Assert.IsTrue(Story.HangarUnlocked);
            Assert.IsTrue(Story.LendForEvent(9, "trial", false));
            Assert.IsFalse(Story.HangarUnlocked, "no Hangar in a loaner, free play too: it can't be sold");
            Assert.AreEqual(9, Session.ShipIndex);
            Assert.IsFalse(Session.Equipment.Any(e => !e.builtIn), "a fresh hull");
            Assert.AreEqual(0, Session.Cargo.Count, "an empty hold");
            Assert.AreEqual("trial", Session.ParkedShip.lentBy);
            Assert.IsFalse(Story.LendForEvent(0, "trial", false), "one loaner at a time");
            Session.Equipment.Add(new ItemStack(3, 1));   // the mission mounts a gun on the loaner
            Assert.IsFalse(Story.ReturnEventLoaner("another"), "only the lending graph's");
            Assert.IsTrue(Story.ReturnEventLoaner("trial"));
            Assert.IsNull(Session.ParkedShip);
            Assert.AreEqual(10, Session.ShipIndex);
            CollectionAssert.AreEqual(new[] { 2, 54 }, Session.Equipment.Where(e => !e.builtIn).Select(e => e.item).ToArray(), "the loaner's gun stays with it");
            Assert.AreEqual(4, Shop.CargoOf(122));
            Assert.AreEqual(37, Session.PlayerHull, "the own ship's damage");
        }

        [Test]
        public void KeepMovesThePlayersItemsBothWays()
        {
            Assert.IsTrue(Story.LendForEvent(9, "trial", true));
            CollectionAssert.AreEqual(new[] { 2, 54 }, Session.Equipment.Where(e => !e.builtIn).Select(e => e.item).ToArray());
            Assert.AreEqual(4, Shop.CargoOf(122));
            Session.Cargo.Add(new ItemStack(41, 2));   // picked up on the loaner
            Assert.IsTrue(Story.ReturnEventLoaner(null));
            Assert.AreEqual(10, Session.ShipIndex);
            CollectionAssert.AreEqual(new[] { 2, 54 }, Session.Equipment.Where(e => !e.builtIn).Select(e => e.item).ToArray());
            Assert.AreEqual(2, Shop.CargoOf(41), "the hold comes back as it is");
        }

        [Test]
        public void TheStorysLoanerIsNeverAnEvents()
        {
            Session.ParkedShip = new ParkedShip { ship = 10 };   // the Valkyrie's
            Assert.IsFalse(Story.LendForEvent(9, "trial", false));
            Assert.IsFalse(Story.ReturnEventLoaner(null));
            Assert.IsNotNull(Session.ParkedShip);
        }

        [Test]
        public void ALoadWithoutTheLendingRunGivesItBack()
        {
            Story.LendForEvent(9, "trial", false);
            Session.GraphQuests.Add(new GraphQuestState { name = "trial", kind = GraphQuestState.KindMission });
            Story.CheckEventLoaner();
            Assert.AreEqual(9, Session.ShipIndex, "the run goes on: the loaner stays");
            Session.GraphQuests.Clear();
            Story.CheckEventLoaner();
            Assert.AreEqual(10, Session.ShipIndex);
            Assert.IsNull(Session.ParkedShip);
        }
    }
}
