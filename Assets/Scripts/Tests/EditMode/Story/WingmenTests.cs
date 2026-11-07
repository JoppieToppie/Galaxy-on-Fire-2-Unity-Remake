// WingmenTests.cs
// Feature: CLAUDE.md "Bar and freelance" (Wingmen: the agent and its 0-2 friends, the 10-minute contract, a dead wingman
// leaves it, the goodbye at the next docking once it ran out).
// Expected values: Reference/research/wingmen_wanted.md §1.1 (Status+0x24 names agent first, +0x2c race, +0x30 contract
// 600 000 ms, +0x28 the agent's portrait, +0xd4 += friends + 1; setWingmen(null) clears names, portrait and contract),
// §1.7 (Level::wingmanDied: the name leaves, the last one ends the contract), §1.8 (expired when the contract < 1),
// §1.10 (they persist across saves). Remake mods: a mod crew's ships (Session.WingmanShips, Modding/README.md "Wingmen").

using System.Collections.Generic;
using GoF2Remake.Data;
using NUnit.Framework;

namespace GoF2Remake.Tests
{
    [Unity.Scripting.LifecycleManagement.NoAutoStaticsCleanup]
    public class WingmenTests : StoryAreaTestBase
    {
        static Agent Hireling(int friends) => new Agent
        {
            name = "Lead", race = 2, offer = AgentOffer.Wingmen, portrait = new[] { 2, 1, 3, 4, 0 },
            wingmen = friends == 0 ? new List<string>() : friends == 1 ? new List<string> { "Friend A" } : new List<string> { "Friend A", "Friend B" },
        };

        [Test]
        public void ContractIsTenMinutes()
        {
            Assert.AreEqual(600000f, Wingmen.ContractMs);
        }

        [Test]
        public void HiringTakesTheAgentAndItsFriends()
        {
            var agent = Hireling(2);
            Wingmen.Hire(agent);
            CollectionAssert.AreEqual(new[] { "Lead", "Friend A", "Friend B" }, Session.Wingmen, "the agent first");
            Assert.AreEqual(2, Session.WingmanRace);
            Assert.AreEqual(600000f, Session.WingmanContractMs);
            CollectionAssert.AreEqual(agent.portrait, Session.WingmanPortrait);
            Assert.AreNotSame(agent.portrait, Session.WingmanPortrait, "a copy");
            Assert.AreEqual(3, Session.WingmenHired, "friends + 1");
            Assert.IsTrue(Wingmen.Hired);
            Assert.IsFalse(Wingmen.Expired);
        }

        [Test]
        public void TheHiredCountAddsUp()
        {
            Wingmen.Hire(Hireling(0));
            Wingmen.Dismiss();
            Wingmen.Hire(Hireling(1));
            Assert.AreEqual(3, Session.WingmenHired, "medal 27's counter");
        }

        [Test]
        public void ExpiresWhenTheContractRunsOut()
        {
            Wingmen.Hire(Hireling(1));
            Session.WingmanContractMs = 1f;
            Assert.IsFalse(Wingmen.Expired);
            Session.WingmanContractMs = 0.5f;
            Assert.IsTrue(Wingmen.Expired, "< 1");
            Assert.IsTrue(Wingmen.Hired, "they keep flying until the next docking");
        }

        [Test]
        public void NotExpiredWithoutWingmen()
        {
            Session.WingmanContractMs = 0f;
            Assert.IsFalse(Wingmen.Hired);
            Assert.IsFalse(Wingmen.Expired);
        }

        [Test]
        public void ADeadWingmanLeavesTheContract()
        {
            Wingmen.Hire(Hireling(2));
            Wingmen.Died("Friend A");
            CollectionAssert.AreEqual(new[] { "Lead", "Friend B" }, Session.Wingmen);
            Assert.AreEqual(600000f, Session.WingmanContractMs, "the others stay");
            Wingmen.Died("Unknown");
            Assert.AreEqual(2, Session.Wingmen.Count);
            Wingmen.Died("Lead");
            Wingmen.Died("Friend B");
            Assert.IsFalse(Wingmen.Hired, "the last one ends it");
            Assert.AreEqual(0f, Session.WingmanContractMs);
        }

        [Test]
        public void DismissClearsTheContract()
        {
            Wingmen.Hire(Hireling(1));
            Wingmen.Dismiss();
            Assert.AreEqual(0, Session.Wingmen.Count);
            Assert.AreEqual(0f, Session.WingmanContractMs);
            Assert.AreEqual(2, Session.WingmenHired, "the statistic stays");
        }

        [Test]
        public void WingmenPersistAcrossSaves()
        {
            Wingmen.Hire(Hireling(1));
            Session.WingmanContractMs = 123456f;
            Session.StationIndex = 57;
            Assert.IsTrue(SaveGame.Save(3));
            Session.ResetNewGame();
            Assert.IsFalse(Wingmen.Hired);
            Assert.IsTrue(SaveGame.Load(3));
            CollectionAssert.AreEqual(new[] { "Lead", "Friend A" }, Session.Wingmen);
            Assert.AreEqual(123456f, Session.WingmanContractMs);
            Assert.AreEqual(2, Session.WingmanRace);
            Assert.AreEqual(2, Session.WingmenHired);
            CollectionAssert.AreEqual(new[] { 2, 1, 3, 4, 0 }, Session.WingmanPortrait);
        }

        [Test]
        public void AModCrewsShipsGoWithItsPilots()
        {
            // remake mods (wingmen.json, Modding.ModWingmen): the agent's crew's ships, in the same order as the names
            var agent = Hireling(2);
            agent.wingmanShips = new List<int> { 5, 6, 7 };
            Wingmen.Hire(agent);
            CollectionAssert.AreEqual(new[] { 5, 6, 7 }, Session.WingmanShips);
            Assert.AreNotSame(agent.wingmanShips, Session.WingmanShips, "a copy");
            Wingmen.Died("Friend A");
            CollectionAssert.AreEqual(new[] { 5, 7 }, Session.WingmanShips, "a dead one's ship leaves with it");
            Session.StationIndex = 57;
            Assert.IsTrue(SaveGame.Save(3));
            Session.ResetNewGame();
            Assert.AreEqual(0, Session.WingmanShips.Count);
            Assert.IsTrue(SaveGame.Load(3));
            CollectionAssert.AreEqual(new[] { 5, 7 }, Session.WingmanShips, "saved");
            Wingmen.Dismiss();
            Assert.AreEqual(0, Session.WingmanShips.Count);
            Wingmen.Hire(Hireling(1));
            Assert.AreEqual(0, Session.WingmanShips.Count, "the game's wingmen: none");
        }
    }
}
