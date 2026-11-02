// EventBarMissionOfferTests.cs
// Feature: CLAUDE.md "Multiplayer" > Event graph bar missions: not every bar offers a graph's bar mission. Its
// "mission chance" (the Start node's Offer chance %, default 35) is rolled per station and kept for
// EventMissions.OfferSeconds, so docking again rolls nothing; once taken at a station that bar drops it for as long. The
// visitor's plate names the mission only once the player has talked to them, like a freelance agent's. Single player: a
// bar mission's "Starts when" holds before any bar offers it, and with "Only once" a completed one isn't offered again.
// Expected values: the rules as documented there (0 = never, 100 = always, the roll stable for its period).

using System.Collections.Generic;
using System.Linq;
using System.Reflection;
using GoF2Remake.Data;
using GoF2Remake.Events;
using Newtonsoft.Json.Linq;
using NUnit.Framework;
using U = GoF2Remake.Tests.EventsTestUtil;

namespace GoF2Remake.Tests
{
    public class EventBarMissionOfferTests : SandboxTest
    {
        [SetUp]
        public void ClearRolls() => EventMissions.ResetOffers();

        [TearDown]
        public void ClearRollsAfter() => EventMissions.ResetOffers();

        static EventRunner.MissionInfo Mission(string name, int chance) =>
            new EventRunner.MissionInfo { name = name, title = name, client = "Keith", chance = chance };

        [Test]
        public void AChanceOfNoneIsNeverOfferedAndAllIsAlways()
        {
            for (int station = 0; station < 50; station++)
            {
                Assert.IsFalse(EventMissions.Offered(Mission("never", 0), station));
                Assert.IsTrue(EventMissions.Offered(Mission("always", 100), station));
            }
        }

        [Test]
        public void AStationsRollHoldsWhenDockingAgain()
        {
            var m = Mission("hideout", 50);
            var first = Enumerable.Range(0, 40).Select(st => EventMissions.Offered(m, st)).ToList();
            for (int again = 0; again < 5; again++)
                CollectionAssert.AreEqual(first, Enumerable.Range(0, 40).Select(st => EventMissions.Offered(m, st)).ToList());
            Assert.IsTrue(first.Contains(true) && first.Contains(false), "50 % over 40 stations offers some and not others");
        }

        [Test]
        public void ATakenMissionLeavesThatBarOnly()
        {
            var m = Mission("hideout", 100);
            EventMissions.Taken("hideout", 78);
            Assert.IsFalse(EventMissions.Offered(m, 78), "taken there: gone from that bar");
            Assert.IsTrue(EventMissions.Offered(m, 70), "still offered elsewhere");
        }

        [Test]
        public void TheDefaultChance()
        {
            Assert.AreEqual(35, EventRunner.MissionInfo.DefaultChance);
            Assert.AreEqual(35, new EventRunner.MissionInfo().chance);
        }

        [Test]
        public void TheScriptsChanceLine()
        {
            Assert.AreEqual(20, Info("mission title T\nmission chance 20\ncomplete\n").chance);
            Assert.AreEqual(100, Info("mission title T\nmission chance 250%\ncomplete\n").chance, "clamped to 100");
            Assert.AreEqual(EventRunner.MissionInfo.DefaultChance, Info("mission title T\ncomplete\n").chance, "none given: the default");
        }

        static EventRunner.MissionInfo Info(string script)
        {
            var flags = BindingFlags.NonPublic | BindingFlags.Static;
            var compile = typeof(EventRunner).GetMethod("Compile", flags, null, new[] { typeof(string), typeof(string).MakeByRefType() }, null);
            var args = new object[] { script, null };
            object steps = compile.Invoke(null, args);
            Assert.IsNotNull(steps, "compiles: " + args[1]);
            return (EventRunner.MissionInfo)typeof(EventRunner).GetMethod("Info", flags).Invoke(null, new[] { steps });
        }

        [Test]
        public void WhoCanTakeItIsALine()
        {
            Assert.AreEqual(EventMissionTakers.OneInUniverse, Info("mission title T\ncomplete\n").takers, "one squad in the universe by default");
            Assert.AreEqual(EventMissionTakers.OnePerStation, Info("mission title T\nmission takers station\ncomplete\n").takers);
            Assert.AreEqual(EventMissionTakers.Anyone, Info("mission title T\nmission takers any\ncomplete\n").takers);
            var g = U.WithStart(("Kind", ((int)EventKind.BarMission).ToString()), ("MissionTitle", "Hideout"),
                                ("MissionTakers", ((int)EventMissionTakers.OnePerStation).ToString()));
            CollectionAssert.Contains(U.Lines(EventGraphScript.Compile(g, new List<EventGraphScript.Problem>())), "mission takers station");
            var quest = new JObject { ["format"] = "gof2quest", ["title"] = "T", ["kind"] = "mission", ["takers"] = "any", ["steps"] = new JArray() };
            string script = QuestFile.Compile(quest, new List<string>());
            StringAssert.Contains("mission takers any", script);
            Assert.AreEqual("any", (string)QuestFile.FromScript(script, "T", new List<string>())["takers"]);
        }

        [Test]
        public void TheGraphsOfferChanceBecomesALine()
        {
            var g = U.WithStart(("Kind", ((int)EventKind.BarMission).ToString()), ("MissionTitle", "Hideout"), ("MissionChance", "20"));
            CollectionAssert.Contains(U.Lines(EventGraphScript.Compile(g, new List<EventGraphScript.Problem>())), "mission chance 20");
            var plain = U.WithStart(("Kind", ((int)EventKind.BarMission).ToString()), ("MissionTitle", "Hideout"));
            Assert.IsFalse(U.Lines(EventGraphScript.Compile(plain, new List<EventGraphScript.Problem>())).Any(l => l.StartsWith("mission chance")),
                           "not set: no line (the default)");
        }

        [Test]
        public void AQuestFilesChanceRoundTrips()
        {
            var quest = new JObject { ["format"] = "gof2quest", ["title"] = "T", ["kind"] = "mission", ["chance"] = 15, ["steps"] = new JArray() };
            string script = QuestFile.Compile(quest, new List<string>());
            StringAssert.Contains("mission chance 15", script);
            Assert.AreEqual(15, (int)QuestFile.FromScript(script, "T", new List<string>())["chance"]);
        }

        // Remake (a modder's request, 2026-11-02), single player: a bar mission's "Starts when" and "Only once".

        [Test]
        public void ABarMissionWaitsForItsStartCondition()
        {
            var m = Mission("courier", 100);
            m.starts = "credits >= 5000";
            Session.Credits = 4999;
            Assert.IsFalse(EventMissions.Startable(m));
            Session.Credits = 5000;
            Assert.IsTrue(EventMissions.Startable(m));
            m.starts = "never";
            Assert.IsFalse(EventMissions.Startable(m), "never: no bar offers it");
            m.starts = "nosuchword > 1";
            Assert.IsFalse(EventMissions.Startable(m), "a broken condition is false");
        }

        [Test]
        public void AnOnlyOnceBarMissionIsDoneOnceCompleted()
        {
            var m = Mission("courier", 100);
            Assert.IsTrue(EventMissions.Startable(m));
            Session.GraphQuestsDone.Add("courier");
            Assert.IsTrue(EventMissions.Startable(m), "without Only once it comes again");
            m.once = true;
            Assert.IsFalse(EventMissions.Startable(m));
        }

        [Test]
        public void StartsAndOnceAreLinesInEveryForm()
        {
            var info = Info("mission kind mission\nmission title T\nmission starts visited(78)\nmission once\ncomplete\n");
            Assert.AreEqual("visited(78)", info.starts);
            Assert.IsTrue(info.once);
            Assert.IsFalse(Info("mission kind mission\nmission title T\ncomplete\n").once);
            var g = U.WithStart(("Kind", ((int)EventKind.BarMission).ToString()), ("MissionTitle", "Hideout"), ("StartsWhen", "rank >= 3"), ("OnlyOnce", "True"));
            var lines = U.Lines(EventGraphScript.Compile(g, new List<EventGraphScript.Problem>()));
            CollectionAssert.Contains(lines, "mission starts rank >= 3");
            CollectionAssert.Contains(lines, "mission once");
            var quest = new JObject { ["format"] = "gof2quest", ["title"] = "T", ["kind"] = "mission", ["startsWhen"] = "rank >= 3", ["once"] = true, ["steps"] = new JArray() };
            string script = QuestFile.Compile(quest, new List<string>());
            StringAssert.Contains("mission starts rank >= 3", script);
            StringAssert.Contains("mission once", script);
            var back = QuestFile.FromScript(script, "T", new List<string>());
            Assert.AreEqual("rank >= 3", (string)back["startsWhen"]);
            Assert.IsTrue((bool)back["once"]);
        }

        [Test]
        public void TheVisitorsPlateNamesTheMissionOnlyOnceTalkedTo()
        {
            var a = new Agent { name = "Mara Kessel", race = 0, offer = AgentOffer.EventMission };
            Assert.AreEqual((Localization.Get(406), ""), LoungeChat.Plate(a), "not talked to: their race");
            a.known = true;
            Assert.AreEqual("Mara Kessel", LoungeChat.Plate(a).name);
        }
    }
}
