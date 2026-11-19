// NetTradeTests.cs
// Feature: CLAUDE.md "Multiplayer" > Trades between players (NetTrade / NetTradeClient): an offer is credits and cargo
// items, at most 16 kinds, never the Courier's secure containers (116) or passenger cabins (117), nor story items
// (Session.Unsaleable, refused by the paying game); the server checks every offer it gets (a bad length, negative
// credits, an unknown or forbidden item, an amount outside 1..100000, the same item twice) and keeps it sorted by item.
// And Bar missions outside a session (NetMissions): solo, the whole reward and wager are the player's, any player may
// deliver, nothing refuses the acceptance.
// Expected values: CLAUDE.md's rules (16 kinds, 116 / 117, story items); hand-built offers.
// NetTrade.TryParse (the server's check) is private: called by reflection, the one entry the checks have without a
// running session (SetOffer needs an open trade between two connected players).

using System.Collections.Generic;
using System.Linq;
using System.Reflection;
using GoF2Remake.Data;
using GoF2Remake.Multiplayer;
using NUnit.Framework;

namespace GoF2Remake.Tests
{
    public class NetTradeTests : SandboxTest
    {
        /// <summary>An offer written the short way (the credits as one int, then item / amount pairs) in the packed form (the
        /// credits 64-bit: low, high).</summary>
        static int[] Wide(int[] shortForm)
        {
            if (shortForm == null || shortForm.Length == 0) return shortForm;
            long credits = shortForm[0];
            var a = new List<int> { (int)(credits & 0xFFFFFFFFL), (int)(credits >> 32) };
            for (int i = 1; i < shortForm.Length; i++) a.Add(shortForm[i]);
            return a.ToArray();
        }

        static NetTrade.Offer Unpack(int[] shortForm) => NetTrade.Offer.Unpack(Wide(shortForm));

        static bool Check(int[] shortForm, out NetTrade.Offer offer)
        {
            var packed = Wide(shortForm);
            var m = typeof(NetTrade).GetMethod("TryParse", BindingFlags.NonPublic | BindingFlags.Static);
            Assert.IsNotNull(m, "NetTrade.TryParse");
            var args = new object[] { packed, null, null };
            bool ok = (bool)m.Invoke(null, args);
            offer = (NetTrade.Offer)args[1];
            if (!ok) Assert.IsNotNull(args[2], "a refusal says what was wrong");
            return ok;
        }

        static List<int> TradeableItems(int n) =>
            Db.Items.Select(i => i.index).Where(i => i >= 0 && i != 116 && i != 117).OrderBy(i => i).Take(n).ToList();

        [Test]
        public void TheLimits()
        {
            Assert.AreEqual(16, NetTrade.MaxKinds);
            Assert.AreEqual(116, Freelance.SecureContainer);
            Assert.AreEqual(117, Freelance.SecureCabin);
        }

        [Test]
        public void WhatMayBeTraded()
        {
            Assert.IsTrue(NetTrade.Tradeable(85));
            Assert.IsFalse(NetTrade.Tradeable(Freelance.SecureContainer), "a Courier's container");
            Assert.IsFalse(NetTrade.Tradeable(Freelance.SecureCabin), "a passenger cabin");
            Assert.IsFalse(NetTrade.Tradeable(-1));
            Assert.IsFalse(NetTrade.Tradeable(100000));
        }

        [Test]
        public void StoryItemsStayWithThePlayer()
        {
            Assert.IsTrue(NetTradeClient.Tradeable(90));
            Session.Unsaleable.Add(90);
            Assert.IsFalse(NetTradeClient.Tradeable(90), "Gunant's Drill made unsaleable by the story");
            Assert.IsTrue(NetTrade.Tradeable(90), "the server's own check leaves story items to the paying game");
        }

        [Test]
        public void CreditsAlone()
        {
            Assert.IsTrue(Check(new[] { 5000 }, out var o));
            Assert.AreEqual(5000, o.credits);
            Assert.IsEmpty(o.items);
            Assert.IsTrue(Check(new[] { 0 }, out o));
            Assert.IsTrue(o.Empty);
        }

        [Test]
        public void ItemsAreSortedByNumber()
        {
            Assert.IsTrue(Check(new[] { 0, 90, 1, 85, 2 }, out var o));
            CollectionAssert.AreEqual(new[] { 85, 90 }, o.items.Select(s => s.item).ToArray());
            CollectionAssert.AreEqual(new[] { 2, 1 }, o.items.Select(s => s.amount).ToArray());
        }

        [Test]
        public void BadOffers()
        {
            Assert.IsFalse(Check(new int[0], out _), "nothing");
            Assert.IsFalse(Check(null, out _));
            Assert.IsFalse(Check(new[] { 0, 85 }, out _), "an item without its amount");
            Assert.IsFalse(Check(new[] { -1 }, out _), "negative credits");
            Assert.IsFalse(Check(new[] { 0, 85, 1, 85, 2 }, out _), "the same item twice");
            Assert.IsFalse(Check(new[] { 0, 116, 1 }, out _), "a secure container");
            Assert.IsFalse(Check(new[] { 0, 117, 1 }, out _), "a passenger cabin");
            Assert.IsFalse(Check(new[] { 0, 100000, 1 }, out _), "no such item");
            Assert.IsFalse(Check(new[] { 0, 85, 0 }, out _), "no units");
            Assert.IsFalse(Check(new[] { 0, 85, -3 }, out _));
            Assert.IsFalse(Check(new[] { 0, 85, NetTrade.MaxUnits + 1 }, out _));
            Assert.IsTrue(Check(new[] { 0, 85, NetTrade.MaxUnits }, out _));
        }

        [Test]
        public void AtMost16Kinds()
        {
            var items = TradeableItems(17);
            Assert.AreEqual(17, items.Count);
            int[] Pack(int n)
            {
                var a = new List<int> { 100 };
                foreach (var i in items.Take(n)) { a.Add(i); a.Add(1); }
                return a.ToArray();
            }
            Assert.IsTrue(Check(Pack(16), out var o));
            Assert.AreEqual(16, o.items.Count);
            Assert.IsFalse(Check(Pack(17), out _));
        }

        [Test]
        public void PackAndUnpack()
        {
            var o = new NetTrade.Offer { credits = 1234 };
            o.items.Add(new ItemStack(85, 3));
            o.items.Add(new ItemStack(90, 1));
            var packed = o.Pack();
            CollectionAssert.AreEqual(new[] { 1234, 0, 85, 3, 90, 1 }, packed);
            var back = NetTrade.Offer.Unpack(packed);
            Assert.IsTrue(back.Same(o));
            Assert.IsTrue(o.Same(back));
        }

        /// <summary>64-bit credits (a Discord report 2026-11-16 about the 32-bit cap): a trade of more than 2 147 483 647.</summary>
        [Test]
        public void CreditsPastThe32BitTop()
        {
            var o = new NetTrade.Offer { credits = 5_000_000_000_000L };
            o.items.Add(new ItemStack(85, 3));
            var back = NetTrade.Offer.Unpack(o.Pack());
            Assert.AreEqual(5_000_000_000_000L, back.credits);
            Assert.IsTrue(back.Same(o));
            Assert.IsTrue(Check(new[] { 0, 85, 1 }, out _));
            var m = typeof(NetTrade).GetMethod("TryParse", BindingFlags.NonPublic | BindingFlags.Static);
            var args = new object[] { new NetTrade.Offer { credits = long.MaxValue }.Pack(), null, null };
            Assert.IsTrue((bool)m.Invoke(null, args));
            Assert.AreEqual(long.MaxValue, ((NetTrade.Offer)args[1]).credits);
        }

        [Test]
        public void UnpackDropsWhatIsntThere()
        {
            var o = Unpack(new[] { -50, 85, 0, 90, 2, 77 });
            Assert.AreEqual(0, o.credits, "no negative credits");
            Assert.AreEqual(1, o.items.Count, "no empty stacks, no half pair");
            Assert.AreEqual(90, o.items[0].item);
            Assert.IsTrue(NetTrade.Offer.Unpack(null).Empty);
            Assert.IsTrue(NetTrade.Offer.Unpack(new int[0]).Empty);
        }

        [Test]
        public void SameComparesEverything()
        {
            var a = Unpack(new[] { 10, 85, 1 });
            Assert.IsFalse(a.Same(Unpack(new[] { 11, 85, 1 })));
            Assert.IsFalse(a.Same(Unpack(new[] { 10, 85, 2 })));
            Assert.IsFalse(a.Same(Unpack(new[] { 10, 84, 1 })));
            Assert.IsFalse(a.Same(Unpack(new[] { 10 })));
            Assert.IsFalse(a.Same(null));
        }

        [Test]
        public void AnOfferDescribed()
        {
            Assert.AreEqual(Localization.Extra("mpTradeNothing", "nothing"), NetTradeClient.Describe(new NetTrade.Offer()));
            var o = Unpack(new[] { 0, 85, 2 });
            StringAssert.StartsWith("2 t ", NetTradeClient.Describe(o));
        }

        [Test]
        public void TheRequestWaitsAMinute()
        {
            Assert.AreEqual(60f, NetTrade.RequestSeconds);
            Assert.AreEqual(15f, NetTrade.PaySeconds);
        }

        // ---- bar missions without a session ----

        [Test]
        public void SoloTheRewardIsAllYours()
        {
            var m = new FreelanceMission { netId = 12345 };
            Assert.AreEqual(30000, NetMissions.SplitReward(m, 30000));
            Assert.AreEqual(5000, NetMissions.Failed(m, 5000));
            Assert.IsTrue(NetMissions.CanDeliver(m));
            Assert.IsNull(NetMissions.AcceptRefusal());
            Assert.AreEqual("", NetMissions.DiscardWarning());
        }
    }
}
