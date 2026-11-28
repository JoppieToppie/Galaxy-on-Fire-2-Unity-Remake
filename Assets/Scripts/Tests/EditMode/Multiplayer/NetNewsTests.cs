// NetNewsTests.cs
// Feature: CLAUDE.md "Multiplayer" > Sector news: the station ticker's multiplayer items, written in English on the
// server; the players' own text (names, faction names, an admin's item) is made tag-free for the rich-text ticker
// (NetNews.Safe: '<' and '>' swapped for look-alikes); "A", "A and B", "A, B and C", "A, B, C and N others"; places
// "Var Hastra (Mido)", the Void; nothing is posted outside a session.
// Expected values: the documented text forms, hand-written names; Var Hastra (78) in the Mido system (CLAUDE.md).

using System.Collections.Generic;
using GoF2Remake.Data;
using GoF2Remake.Multiplayer;
using NUnit.Framework;

namespace GoF2Remake.Tests
{
    public class NetNewsTests : SandboxTest
    {
        [Test]
        public void FreightNewsRequiresMatchingValidSystem()
        {
            Assert.IsTrue(NetNews.VisibleInSystem(NetNews.Kind.Freight, 2, 2));
            Assert.IsFalse(NetNews.VisibleInSystem(NetNews.Kind.Freight, 2, 3));
            Assert.IsFalse(NetNews.VisibleInSystem(NetNews.Kind.Freight, -1, -1));
            Assert.IsTrue(NetNews.VisibleInSystem(NetNews.Kind.Commerce, 2, 3));
            Assert.IsTrue(NetNews.VisibleInSystem(NetNews.Kind.War, 2, 3));
            Assert.IsTrue(NetNews.NeedsDestinationCopy(2, 3));
            Assert.IsFalse(NetNews.NeedsDestinationCopy(2, 2));
            Assert.IsFalse(NetNews.NeedsDestinationCopy(2, -1));
            Assert.IsTrue(NetNews.IsLegacyFreight("Freight bound for Mido escaped a pirate attack near Nehébru. Player pirate kills: 2."));
            Assert.IsFalse(NetNews.IsLegacyFreight("Market prices changed."));
        }

        [Test]
        public void SafeTextHasNoTags()
        {
            Assert.AreEqual("‹b›Bob‹/b›", NetNews.Safe("<b>Bob</b>"));
            Assert.AreEqual("plain", NetNews.Safe("plain"));
            Assert.AreEqual("", NetNews.Safe(null));
            Assert.IsFalse(NetNews.Safe("<color=red>x</color>").Contains("<"));
        }

        [Test]
        public void FactionNames()
        {
            Assert.AreEqual("[ABC] Alpha Wing", NetNews.FactionName("ABC", "Alpha Wing"));
            Assert.AreEqual("[‹X›] ‹b›", NetNews.FactionName("<X>", "<b>"));
        }

        [Test]
        public void ListsOfNames()
        {
            Assert.AreEqual("Unknown pilots", NetNews.Names(new List<string>()));
            Assert.AreEqual("Ann", NetNews.Names(new List<string> { "Ann" }));
            Assert.AreEqual("Ann and Bob", NetNews.Names(new List<string> { "Ann", "Bob" }));
            Assert.AreEqual("Ann, Bob and Cid", NetNews.Names(new List<string> { "Ann", "Bob", "Cid" }));
            Assert.AreEqual("Ann, Bob, Cid and 1 other", NetNews.Names(new List<string> { "Ann", "Bob", "Cid", "Dee" }));
            Assert.AreEqual("Ann, Bob, Cid and 2 others", NetNews.Names(new List<string> { "Ann", "Bob", "Cid", "Dee", "Eve" }));
        }

        [Test]
        public void NamesAreCountedOnce()
        {
            Assert.AreEqual("Ann and Bob", NetNews.Names(new List<string> { "Ann", "", null, "Bob", "Ann" }));
            Assert.AreEqual("‹i›", NetNews.Names(new List<string> { "<i>" }));
        }

        [Test]
        public void ANameWithATagIsCountedOnceToo()
        {
            // Names() skips a name already listed, but compares the raw name with the listed (Safe) ones, so a name holding
            // '<' or '>' is listed twice. Pilot names are cleaned of both (NetGame.Clean), so it only shows with other text.
            Assert.AreEqual("‹i›", NetNews.Names(new List<string> { "<i>", "<i>" }));
        }

        [Test]
        public void Places()
        {
            Assert.AreEqual("the Void", NetNews.Place(Session.VoidOrbit));
            Assert.AreEqual("station 99999", NetNews.Place(99999));
            var st = NetGame.Db.Stations.Find(s => s.index == 78);
            Assert.AreEqual(string.IsNullOrEmpty(st.systemName) ? st.name : st.name + " (" + st.systemName + ")", NetNews.Place(78));
        }

        [Test]
        public void VarHastraIsInMido()
        {
            EventsTestUtil.RequireEnglish();
            Assert.AreEqual("Var Hastra (Mido)", NetNews.Place(78));
        }

        [Test]
        public void RaidersInHeadlines()
        {
            Assert.AreEqual("pirate", NetNews.Raiders(GoF2Remake.Flight.Standing.Pirate));
            Assert.AreEqual("Void", NetNews.Raiders(GoF2Remake.Flight.Standing.Void));
            Assert.AreEqual("Specter", NetNews.Raiders(GoF2Remake.Flight.Standing.Specter));
            Assert.AreEqual(Localization.Get(406 + 1), NetNews.Raiders(1), "a race by its name (text 406 + race)");
            Assert.AreEqual("hostile", NetNews.Raiders(42));
        }

        [Test]
        public void NothingIsPostedOutsideASession()
        {
            Assert.IsFalse(NetNews.Post(NetNews.Kind.Galnet, "Test item"));
        }

        [Test]
        public void TheTickerIsEmptyOutsideASession()
        {
            Assert.AreEqual("", NetNews.Ticker(78));
        }

        [Test]
        public void TheLimits()
        {
            Assert.AreEqual(3, NetNews.MinDefenseKills, "a defence report needs 3+ kills");
        }
    }
}
