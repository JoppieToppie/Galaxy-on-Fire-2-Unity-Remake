// EventGraphCompilerTests.cs
// Feature: CLAUDE.md "Multiplayer" > Event graphs: EventGraphScript compiles a graph (nodes, wires, blackboard variables)
// to EventRunner's line form: the Start node's description and Mission settings first ("mission" lines; a quest's
// "mission kind quest" / "mission starts"), "set" / "param" for the variables, then the steps along the flow wires,
// blocks indented and closed by "end"; values become expressions with the fewest brackets (the runner's precedence:
// or < and < not < comparisons < + - < * / %); problems name their node.
// Expected values: hand-written graphs and the line form documented in CLAUDE.md / EventGraphScript's header and the
// precedence table (EventRunner.Parser), worked out by hand.

using System.Collections.Generic;
using System.Linq;
using GoF2Remake.Events;
using NUnit.Framework;
using U = GoF2Remake.Tests.EventsTestUtil;

namespace GoF2Remake.Tests
{
    public class EventGraphCompilerTests : SandboxTest
    {
        static List<string> CompileLines(EventGraphData g, out List<EventGraphScript.Problem> problems)
        {
            problems = new List<EventGraphScript.Problem>();
            return U.Lines(EventGraphScript.Compile(g, problems));
        }

        [Test]
        public void NoStartNodeIsAnError()
        {
            var g = new EventGraphData();
            U.Node(g, "w", "EventWaitNode", ("Seconds", "5"));
            var problems = new List<EventGraphScript.Problem>();
            Assert.AreEqual("", EventGraphScript.Compile(g, problems));
            Assert.IsNotEmpty(U.Errors(problems));
        }

        [Test]
        public void TwoStartNodesAreAnError()
        {
            var g = U.WithStart();
            U.Node(g, "start2", "EventStartNode", ("Description", ""));
            CompileLines(g, out var problems);
            Assert.IsTrue(U.Errors(problems).Any(p => p.nodeId == "start2"));
        }

        [Test]
        public void AWreckSpawnAndItsHackedTrigger()
        {
            // Spawn "wreck": a freighter race (Vossk = 2 in EventRace), no count or behaviour; On Wreck Hacked = "on hacked".
            var g = U.WithStart();
            U.Node(g, "s", "EventSpawnNode", ("Players", "@a"), ("Ship", "wreck"), ("Race", "2"), ("Count", "3"), ("Behaviour", "0"),
                   ("Name", "Hulk"), ("At", "0 0 20000"));
            U.Wire(g, "start", "Next", "s", "In");
            U.Node(g, "on", "EventOnNode", ("Trigger", ((int)EventTrigger.WreckHacked).ToString()));
            U.Node(g, "w", "EventWaitNode", ("Seconds", "1"));
            U.Wire(g, "on", "Do", "w", "In");
            var lines = CompileLines(g, out var problems);
            Assert.IsEmpty(U.Errors(problems), U.Describe(problems));
            CollectionAssert.Contains(lines, "spawn @a wreck vossk named Hulk at 0 0 20000");
            CollectionAssert.Contains(lines, "on hacked");
        }

        [Test]
        public void TheShipScannedTrigger()
        {
            // On Ship Scanned = "on scanned" (remake mods: the player's scanner locked a ship; its name as %victim%).
            var g = U.WithStart();
            U.Node(g, "on", "EventOnNode", ("Trigger", ((int)EventTrigger.ShipScanned).ToString()));
            U.Node(g, "w", "EventWaitNode", ("Seconds", "1"));
            U.Wire(g, "on", "Do", "w", "In");
            var lines = CompileLines(g, out var problems);
            Assert.IsEmpty(U.Errors(problems), U.Describe(problems));
            CollectionAssert.Contains(lines, "on scanned");
        }

        [Test]
        public void AWaitFollowsStart()
        {
            var g = U.WithStart();
            U.Node(g, "w", "EventWaitNode", ("Seconds", "5"));
            U.Wire(g, "start", "Next", "w", "In");
            var lines = CompileLines(g, out var problems);
            Assert.IsEmpty(U.Errors(problems), U.Describe(problems));
            CollectionAssert.AreEqual(new[] { "wait 5" }, lines);
        }

        [Test]
        public void EveryStepLineCarriesItsNodeMarker()
        {
            var g = U.WithStart();
            U.Node(g, "w", "EventWaitNode", ("Seconds", "2.5"));
            U.Wire(g, "start", "Next", "w", "In");
            string script = EventGraphScript.Compile(g);
            StringAssert.Contains("wait 2.5  #@w", script);
        }

        [Test]
        public void TheDescriptionBecomesComments()
        {
            var g = new EventGraphData();
            U.Node(g, "start", "EventStartNode", ("Description", "First line\nSecond line"));
            string script = EventGraphScript.Compile(g);
            StringAssert.StartsWith("# First line\n# Second line\n", script);
        }

        [Test]
        public void StepsChainInOrder()
        {
            var g = U.WithStart();
            U.Node(g, "a", "EventWaitNode", ("Seconds", "1"));
            U.Node(g, "b", "EventStopNode");
            U.Wire(g, "start", "Next", "a", "In");
            U.Wire(g, "a", "Next", "b", "In");
            CollectionAssert.AreEqual(new[] { "wait 1", "stop" }, CompileLines(g, out _));
        }

        [Test]
        public void AnUnconnectedStepIsAWarning()
        {
            var g = U.WithStart();
            U.Node(g, "lost", "EventStopNode");
            var lines = CompileLines(g, out var problems);
            Assert.IsEmpty(lines);
            Assert.IsTrue(problems.Any(p => p.warning && p.nodeId == "lost"));
        }

        [Test]
        public void AStepReachedFromTwoPlacesIsAnError()
        {
            var g = U.WithStart();
            U.Node(g, "if", "EventIfNode", ("Condition", "1"));
            U.Node(g, "s", "EventStopNode");
            U.Wire(g, "start", "Next", "if", "In");
            U.Wire(g, "if", "Then", "s", "In");
            U.Wire(g, "if", "Next", "s", "In");
            CompileLines(g, out var problems);
            Assert.IsTrue(U.Errors(problems).Any(p => p.nodeId == "s"));
        }

        [Test]
        public void MathKeepsThePrecedenceWithFewestBrackets()
        {
            // (1 + 2) * 4: the sum needs brackets inside a product; 5 - (1 + 2): the right operand of a minus needs them too;
            // 6 + 1 * 2: a product inside a sum needs none.
            var g = U.WithStart();
            U.Node(g, "sum", "EventMathNode", ("Op", "0"), ("A", "1"), ("B", "2"));
            U.Node(g, "mul", "EventMathNode", ("Op", "2"), ("A", "0"), ("B", "4"));
            U.Wire(g, "sum", "Out", "mul", "A");
            U.Node(g, "w1", "EventWaitNode", ("Seconds", "0"));
            U.Wire(g, "mul", "Out", "w1", "Seconds");

            U.Node(g, "sum2", "EventMathNode", ("Op", "0"), ("A", "1"), ("B", "2"));
            U.Node(g, "sub", "EventMathNode", ("Op", "1"), ("A", "5"), ("B", "0"));
            U.Wire(g, "sum2", "Out", "sub", "B");
            U.Node(g, "w2", "EventWaitNode", ("Seconds", "0"));
            U.Wire(g, "sub", "Out", "w2", "Seconds");

            U.Node(g, "mul3", "EventMathNode", ("Op", "2"), ("A", "1"), ("B", "2"));
            U.Node(g, "add3", "EventMathNode", ("Op", "0"), ("A", "6"), ("B", "0"));
            U.Wire(g, "mul3", "Out", "add3", "B");
            U.Node(g, "w3", "EventWaitNode", ("Seconds", "0"));
            U.Wire(g, "add3", "Out", "w3", "Seconds");

            U.Wire(g, "start", "Next", "w1", "In");
            U.Wire(g, "w1", "Next", "w2", "In");
            U.Wire(g, "w2", "Next", "w3", "In");
            var lines = CompileLines(g, out var problems);
            Assert.IsEmpty(U.Errors(problems), U.Describe(problems));
            CollectionAssert.AreEqual(new[] { "wait (1 + 2) * 4", "wait 5 - (1 + 2)", "wait 6 + 1 * 2" }, lines);
        }

        [Test]
        public void MinAndMaxAreFunctions()
        {
            var g = U.WithStart();
            U.Node(g, "m", "EventMathNode", ("Op", "6"), ("A", "3"), ("B", "9"));   // Max
            U.Node(g, "w", "EventWaitNode", ("Seconds", "0"));
            U.Wire(g, "m", "Out", "w", "Seconds");
            U.Wire(g, "start", "Next", "w", "In");
            CollectionAssert.AreEqual(new[] { "wait max(3, 9)" }, CompileLines(g, out _));
        }

        [Test]
        public void AnIfWithACompareAndAGameValue()
        {
            var g = U.WithStart();
            U.Node(g, "state", "EventStateNode", ("State", "0"));                   // Enemies
            U.Node(g, "cmp", "EventCompareNode", ("Op", "4"), ("A", "0"), ("B", "0"));   // Greater
            U.Wire(g, "state", "Out", "cmp", "A");
            U.Node(g, "if", "EventIfNode", ("Condition", "0"));
            U.Wire(g, "cmp", "Out", "if", "Condition");
            U.Node(g, "stop", "EventStopNode");
            U.Node(g, "w", "EventWaitNode", ("Seconds", "3"));
            U.Wire(g, "start", "Next", "if", "In");
            U.Wire(g, "if", "Then", "stop", "In");
            U.Wire(g, "if", "Else", "w", "In");
            string script = EventGraphScript.Compile(g);
            CollectionAssert.AreEqual(new[] { "if enemies > 0", "stop", "else", "wait 3", "end" }, U.Lines(script));
            StringAssert.Contains("\n  stop", script, "the block is indented");
            Assert.IsNull(EventRunner.Check(script));
        }

        [Test]
        public void LogicAndNotTakeConditions()
        {
            var g = U.WithStart();
            U.Node(g, "and", "EventLogicNode", ("Op", "0"), ("A", "1"), ("B", "0"));
            U.Node(g, "not", "EventNotNode", ("Input", "0"));
            U.Wire(g, "and", "Out", "not", "Input");
            U.Node(g, "wu", "EventWaitUntilNode", ("Condition", "0"), ("Timeout", "0"));
            U.Wire(g, "not", "Out", "wu", "Condition");
            U.Wire(g, "start", "Next", "wu", "In");
            CollectionAssert.AreEqual(new[] { "wait until not (1 and 0)" }, CompileLines(g, out _));
        }

        [Test]
        public void AWaitUntilWithATimeout()
        {
            var g = U.WithStart();
            U.Node(g, "wu", "EventWaitUntilNode", ("Condition", "1"), ("Timeout", "30"));
            U.Wire(g, "start", "Next", "wu", "In");
            CollectionAssert.AreEqual(new[] { "wait until 1 timeout 30" }, CompileLines(g, out _));
        }

        [Test]
        public void VariablesAndSettings()
        {
            var g = U.WithStart();
            g.variables.Add(new EventGraphData.Variable { name = "Score", value = 3, kind = 0 });
            g.variables.Add(new EventGraphData.Variable { name = "Wave Count", value = 5, kind = 1, setting = true });
            string script = EventGraphScript.Compile(g);
            var lines = U.Lines(script);
            Assert.Contains("set score = 3", lines);
            Assert.Contains("param wave_count = 5", lines, "an input variable is a setting; spaces become _");
            var settings = EventRunner.Settings(script);
            Assert.AreEqual(1, settings.Count);
            Assert.AreEqual("wave_count", settings[0].name);
            Assert.AreEqual("5", settings[0].value);
        }

        [Test]
        public void AQuestStartNode()
        {
            var g = U.WithStart(("Kind", ((int)EventKind.StoryMission).ToString()), ("MissionTitle", "Lost cargo"), ("StartsWhen", "visited(78)"), ("MissionReward", "0"));
            var lines = CompileLines(g, out var problems);
            Assert.IsEmpty(U.Errors(problems), U.Describe(problems));
            Assert.Contains("mission title Lost cargo", lines);
            Assert.Contains("mission kind quest", lines);
            Assert.Contains("mission starts visited(78)", lines);
        }

        [Test]
        public void ABarMissionNeedsItsTitle()
        {
            var g = U.WithStart(("Kind", ((int)EventKind.BarMission).ToString()), ("MissionTitle", ""));
            CompileLines(g, out var problems);
            Assert.IsTrue(U.Errors(problems).Any(p => p.nodeId == "start"));
        }

        [Test]
        public void ABarMissionsLines()
        {
            var g = U.WithStart(("Kind", ((int)EventKind.BarMission).ToString()), ("MissionTitle", "Clear the hideout"),
                                ("MissionOffer", "Line one\nLine two"), ("MissionReward", "25000"), ("MinPlayers", "2"), ("MaxPlayers", "3"));
            var lines = CompileLines(g, out var problems);
            Assert.IsEmpty(U.Errors(problems), U.Describe(problems));
            Assert.Contains("mission kind mission", lines);
            Assert.Contains("mission title Clear the hideout", lines);
            Assert.Contains("mission offer Line one\\nLine two", lines, "the offer's lines escaped as \\n");
            Assert.Contains("mission reward 25000", lines);
            Assert.Contains("mission players 2 3", lines);
        }

        [Test]
        public void TheDefaultPilotCountsOfABarMission()
        {
            var g = U.WithStart(("MissionTitle", "Titled event"), ("MissionReward", "0"));
            Assert.Contains("mission players 1 4", CompileLines(g, out _));
        }

        [Test]
        public void ACheckpointInsideABlockIsAnError()
        {
            var g = U.WithStart(("Kind", ((int)EventKind.StoryMission).ToString()), ("MissionReward", "0"));
            U.Node(g, "if", "EventIfNode", ("Condition", "1"));
            U.Node(g, "cp", "EventCheckpointNode", ("Name", "inside"));
            U.Wire(g, "start", "Next", "if", "In");
            U.Wire(g, "if", "Then", "cp", "In");
            CompileLines(g, out var problems);
            Assert.IsTrue(U.Errors(problems).Any(p => p.nodeId == "cp"));
        }

        [Test]
        public void ACheckpointsNameLosesItsSpaces()
        {
            var g = U.WithStart(("Kind", ((int)EventKind.StoryMission).ToString()), ("MissionReward", "0"));
            U.Node(g, "cp", "EventCheckpointNode", ("Name", "met the trader"));
            U.Wire(g, "start", "Next", "cp", "In");
            Assert.Contains("checkpoint met_the_trader", CompileLines(g, out _));
        }

        [Test]
        public void AnAskNodesLine()
        {
            var g = U.WithStart();
            U.Node(g, "ask", "EventAskNode", ("Players", "@a"), ("Seconds", "20"), ("Question", "Ready?"), ("Speaker", "Keith"),
                   ("Answer 1", "Yes"), ("Answer 2", "No"), ("Answer 3", ""), ("Answer 4", ""));
            U.Node(g, "s", "EventStopNode");
            U.Wire(g, "start", "Next", "ask", "In");
            U.Wire(g, "ask", "Answer 2", "s", "In");
            string script = EventGraphScript.Compile(g);
            CollectionAssert.AreEqual(new[] { "ask @a 20 | Keith : Ready? | Yes | No", "answer 2", "stop", "end", "end" }, U.Lines(script));
            Assert.IsNull(EventRunner.Check(script));
        }

        [Test]
        public void AnAskNeedsTwoAnswers()
        {
            var g = U.WithStart();
            U.Node(g, "ask", "EventAskNode", ("Players", "@a"), ("Seconds", "20"), ("Question", "Ready?"),
                   ("Answer 1", "Yes"), ("Answer 2", ""), ("Answer 3", ""), ("Answer 4", ""));
            U.Wire(g, "start", "Next", "ask", "In");
            CompileLines(g, out var problems);
            Assert.IsTrue(U.Errors(problems).Any(p => p.nodeId == "ask"));
        }

        [Test]
        public void ACommentNodeWritesCommentLines()
        {
            var g = U.WithStart();
            U.Node(g, "c", "EventCommentNode", ("Text", "one\ntwo"));
            U.Wire(g, "start", "Next", "c", "In");
            string script = EventGraphScript.Compile(g);
            StringAssert.Contains("# one", script);
            StringAssert.Contains("# two", script);
            Assert.IsEmpty(U.Lines(script), "comments only");
        }

        [Test]
        public void AHashInAnExpressionIsAWarning()
        {
            var g = U.WithStart();
            U.Node(g, "e", "EventExpressionNode", ("Text", "1 # 2"));
            U.Node(g, "w", "EventWaitNode", ("Seconds", "0"));
            U.Wire(g, "e", "Out", "w", "Seconds");
            U.Wire(g, "start", "Next", "w", "In");
            CompileLines(g, out var problems);
            Assert.IsTrue(problems.Any(p => p.warning && p.nodeId == "e"));
        }

        [Test]
        public void AnUnknownNodeKindIsAnError()
        {
            var g = U.WithStart();
            U.Node(g, "x", "EventFromTheFutureNode");
            U.Wire(g, "start", "Next", "x", "In");
            CompileLines(g, out var problems);
            Assert.IsTrue(U.Errors(problems).Any(p => p.nodeId == "x"));
        }

        [Test]
        public void TheFreeForAllAndScoreNodes()
        {
            var g = U.WithStart();
            U.Node(g, "ffa", "EventFreeForAllNode", ("On", "1"));
            U.Node(g, "score", "EventScoreNode", ("By", ((int)EventScoreBy.Points).ToString()));
            U.Node(g, "win", "EventWinnerNode", ("By", ((int)EventWinnerBy.Score).ToString()));
            U.Wire(g, "start", "Next", "ffa", "In");
            U.Wire(g, "ffa", "Next", "score", "In");
            U.Wire(g, "score", "Next", "win", "In");
            CollectionAssert.AreEqual(new[] { "pvp on", "score points", "winner" }, CompileLines(g, out _));
        }

        [Test]
        public void NumbersAreInvariantAndShort()
        {
            Assert.AreEqual("3", EventGraphScript.Number(3.0));
            Assert.AreEqual("2.5", EventGraphScript.Number(2.5));
            Assert.AreEqual("-1.25", EventGraphScript.Number(-1.25));
            Assert.AreEqual("0", EventGraphScript.Number(double.NaN));
            Assert.AreEqual("0", EventGraphScript.Number(double.PositiveInfinity));
            Assert.AreEqual("0", EventGraphScript.Number(1e-9), "beyond six decimals");
        }

        [Test]
        public void NodeKindsReadAsWords()
        {
            Assert.AreEqual("Wait Until", EventGraphScript.Display("EventWaitUntilNode"));
            Assert.AreEqual("Get Orbit", EventGraphScript.Display("EventGetOrbitNode"));
            Assert.AreEqual("", EventGraphScript.Display(null));
        }

        [Test]
        public void ThePrecedenceLevelsAreOrdered()
        {
            Assert.Less(EventGraphScript.Or, EventGraphScript.And);
            Assert.Less(EventGraphScript.And, EventGraphScript.Not);
            Assert.Less(EventGraphScript.Not, EventGraphScript.Compare);
            Assert.Less(EventGraphScript.Compare, EventGraphScript.Sum);
            Assert.Less(EventGraphScript.Sum, EventGraphScript.Product);
            Assert.Less(EventGraphScript.Product, EventGraphScript.Unary);
            Assert.Less(EventGraphScript.Unary, EventGraphScript.Atom);
        }
    }
}
