// QuestGraphTests.cs
// Feature: CLAUDE.md "Mods" > Mod Builder: quests as step trees and node graphs. Every step type of Events.QuestSteps makes a
// line EventRunner reads; blocks (If / While / Repeat / At the same time / Every / When / Ask / Vote / Random) nest; a
// Unity-made event graph turned into steps (QuestFile.FromGraphFile) compiles back to the same script, its nodes where they
// were; the graph editor's wiring (QuestGraph: Connect / Cut / Detach / Delete, never a block into itself); value nodes wired
// into fields become the expression; {variable} stays in a text; the project's translations and backdrop.json.
// Expected values: EventRunner's line form (Modding/README.md "Quests made in the Mod Builder"), the editor templates in
// Scripts/Editor/Events/Templates.

using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Reflection;
using GoF2Remake.Events;
using GoF2Remake.Modding;
using Newtonsoft.Json.Linq;
using NUnit.Framework;

namespace GoF2Remake.Tests
{
    public class QuestGraphTests : SavesModsTest
    {
        static string RunnerError(string script)
        {
            var compile = typeof(EventRunner).GetMethod("Compile", BindingFlags.NonPublic | BindingFlags.Static, null, new[] { typeof(string), typeof(string).MakeByRefType() }, null);
            var args = new object[] { script, null };
            var steps = compile.Invoke(null, args);
            return steps == null ? (string)args[1] ?? "no steps" : (string)args[1];
        }

        static List<string> Lines(string script) => script.Replace("\r", "").Split('\n').Select(l =>
        {
            int h = l.IndexOf('#');
            return (h >= 0 ? l.Substring(0, h) : l).Trim();
        }).Where(l => l.Length > 0).ToList();

        static JObject Quest(params JObject[] steps) => new JObject { ["format"] = "gof2quest", ["title"] = "T", ["kind"] = "quest", ["steps"] = new JArray(steps) };

        [Test]
        public void EveryStepTypeCompilesAndRuns()
        {
            foreach (var t in QuestSteps.Types)
            {
                var st = QuestSteps.New(t.type);
                // The fields a step can't do without.
                switch (t.type)
                {
                    case "dialog": st["pages"] = new JArray(new JObject { ["speaker"] = "Keith", ["text"] = "Hi" }); break;
                    case "radio": st["speaker"] = "Keith"; st["text"] = "Hi"; break;
                    case "goto": case "waypoint": case "questorbit": case "teleport": st["station"] = 78; break;
                    case "system": st["system"] = 15; break;
                    case "fight": st["ships"] = new JArray(new JObject { ["ship"] = 2 }); break;
                    case "cargo": st["item"] = 22; break;
                    case "reward": st["credits"] = 100; break;
                    case "ask": case "vote": st["question"] = "Go?"; break;
                    case "startquest": st["quest"] = "next"; break;
                    case "startevent": st["event"] = "waves"; break;
                    case "spawn": st["ship"] = 2; break;
                    case "shipgoto": case "shiproute": case "shipfollow": case "shipattack": case "shipaction": st["ship"] = "Raider"; break;
                    case "ship": st["hull"] = 10; break;
                    case "set": st["variable"] = "x"; break;
                    case "command": st["line"] = "heal @a"; break;
                    case "note": st["text"] = "a note"; break;
                    case "title": st["title"] = "Hi"; break;
                    case "waituntil": case "if": case "while": st["condition"] = "1"; break;
                }
                var problems = new List<string>();
                string script = QuestFile.Compile(Quest(st), problems);
                CollectionAssert.IsEmpty(problems, t.type);
                Assert.IsNull(RunnerError(script), t.type + ":\n" + script);
            }
        }

        [Test]
        public void AWormholeStepTakesAVariableDestinationAndComesBack()
        {
            var open = QuestSteps.New("wormhole");
            open["station"] = 78;
            open["at"] = "0 0 50000";
            open["to"] = "{dest}";
            open["seconds"] = 30;
            var toVoid = QuestSteps.New("wormhole");
            toVoid["station"] = 78;
            toVoid["void"] = true;
            var close = QuestSteps.New("wormhole");
            var q = Quest(open, toVoid, close);
            q["variables"] = new JArray(new JObject { ["name"] = "dest", ["value"] = 70 });
            var problems = new List<string>();
            string script = QuestFile.Compile(q, problems);
            CollectionAssert.IsEmpty(problems, script);
            var lines = Lines(script);
            foreach (var l in new[] { "wormhole @a 78 0 0 50000 to {dest} for 30", "wormhole @a 78 to void", "wormhole @a close" })
                CollectionAssert.Contains(lines, l, script);
            Assert.IsNull(RunnerError(script), script);
            // A Unity graph's line back to the step.
            var back = QuestFile.FromScript(script, "T", new List<string>());
            var steps = ((JArray)back["steps"]).OfType<JObject>().Where(s => (string)s["type"] == "wormhole").ToList();
            Assert.AreEqual(3, steps.Count, back.ToString());
            Assert.AreEqual("0 0 50000", (string)steps[0]["at"]);
            Assert.AreEqual("{dest}", (string)steps[0]["to"]);
            Assert.AreEqual(30, (int)steps[0]["seconds"]);
            Assert.IsTrue((bool)steps[1]["void"]);
            Assert.IsNull(steps[2]["station"], "close");
        }

        [Test]
        public void BlocksNest()
        {
            var iff = QuestSteps.New("if");
            iff["condition"] = "kills > 2";
            ((JArray)iff["then"]).Add(new JObject { ["type"] = "title", ["title"] = "Ace" });
            ((JArray)iff["else"]).Add(new JObject { ["type"] = "wait", ["seconds"] = 1 });
            var rep = QuestSteps.New("repeat");
            rep["count"] = "waves";
            rep["as"] = "wave";
            ((JArray)rep["steps"]).Add(iff);
            var ask = QuestSteps.New("ask");
            ask["question"] = "Again?";
            ((JArray)ask["answers"][0]["steps"]).Add(new JObject { ["type"] = "reward", ["credits"] = 5 });
            var random = QuestSteps.New("random");
            ((JArray)random["branches"][1]["steps"]).Add(new JObject { ["type"] = "fail" });
            var on = QuestSteps.New("on");
            on["trigger"] = "near";
            on["station"] = 78;
            on["at"] = "0 0 100";
            ((JArray)on["steps"]).Add(new JObject { ["type"] = "title", ["title"] = "Close", ["who"] = "@trigger" });
            var q = Quest(on, rep, ask, random);
            q["variables"] = new JArray(new JObject { ["name"] = "waves", ["value"] = 3, ["setting"] = true });
            var problems = new List<string>();
            string script = QuestFile.Compile(q, problems);
            CollectionAssert.IsEmpty(problems);
            var lines = Lines(script);
            foreach (var l in new[] { "param waves = 3", "on near 78 0 0 100 500", "title @trigger Close", "repeat waves as wave", "if kills > 2", "else", "title @a Ace",
                                      "ask @a 30 | Again? | Yes | No", "answer 1", "reward @a 5", "set _qr1 = random(1, 2)", "if _qr1 <= 1", "fail" })
                CollectionAssert.Contains(lines, l, script);
            Assert.IsNull(RunnerError(script), script);
        }

        [Test]
        public void ACheckpointInsideABlockIsAProblem()
        {
            var iff = QuestSteps.New("if");
            iff["condition"] = "1";
            ((JArray)iff["then"]).Add(new JObject { ["type"] = "checkpoint" });
            var problems = new List<string>();
            QuestFile.Compile(Quest(iff), problems);
            Assert.AreEqual(1, problems.Count);
        }

        [Test]
        public void UnityGraphsComeBackTheSame()
        {
            string dir = "Assets/Scripts/Editor/Events/Templates";
            var files = Directory.GetFiles(dir, "*.gof2event");
            Assert.Greater(files.Length, 5);
            foreach (var f in files)
            {
                string yaml = File.ReadAllText(f);
                string original = EventGraphScript.FromFile(yaml);
                var notes = new List<string>();
                var graphProblems = new List<EventGraphScript.Problem>();
                var json = QuestFile.FromGraphFile(yaml, "T", notes, graphProblems);
                Assert.IsNotNull(json, f);
                Assert.IsTrue(QuestGraph.HasPos(json, "startPos"), f);
                Assert.IsTrue(QuestGraph.AllSteps(json).Any(s => QuestGraph.HasPos(s)), f + ": the nodes' places");
                var problems = new List<string>();
                string back = QuestFile.Compile(json, problems);
                CollectionAssert.IsEmpty(problems, f);
                // The same lines (an old titled graph says it is a bar mission: "mission kind mission").
                var a = Lines(original);
                var b = Lines(back).Where(l => !(l == "mission kind mission" && !a.Contains(l))).ToList();
                CollectionAssert.AreEquivalent(a, b, f + "\n" + back);
                Assert.IsNull(RunnerError(back), f);
                // Only the forms no step writes stay Command steps.
                Assert.LessOrEqual(QuestGraph.AllSteps(json).Count(s => (string)s["type"] == "command"), 2, f);
            }
        }

        static JObject S(string type) => new JObject { ["type"] = type };

        [Test]
        public void WiresMoveTheSteps()
        {
            JObject a = S("wait"), b = S("wait"), c = S("wait"), d = S("title");
            var iff = QuestSteps.New("if");
            iff["condition"] = "1";
            var q = Quest(a, b, iff);
            // b's Next to d (loose): what followed b (the If) goes loose.
            QuestGraph.AddLoose(q, d);
            Assert.IsNull(QuestGraph.Incoming(d).port);
            Assert.IsTrue(QuestGraph.Connect(q, b, QuestGraph.Next, d));
            CollectionAssert.AreEqual(new[] { a, b, d }, QuestGraph.Root(q));
            Assert.AreEqual(1, QuestGraph.Loose(q).Count);
            // The If's Then to c (loose) and the Start to the If: the old steps go loose.
            QuestGraph.AddLoose(q, c);
            Assert.IsTrue(QuestGraph.Connect(q, iff, "then", c));
            Assert.AreEqual((iff, "then"), QuestGraph.Incoming(c));
            Assert.AreEqual(c, QuestGraph.Target(q, iff, "then"));
            Assert.IsTrue(QuestGraph.Connect(q, null, QuestGraph.Start, iff));
            Assert.AreEqual(iff, QuestGraph.Root(q).Single());
            // Never a block into itself.
            Assert.IsFalse(QuestGraph.Connect(q, c, QuestGraph.Next, iff));
            // Cut and detach.
            QuestGraph.Cut(q, iff, "then");
            Assert.IsNull(QuestGraph.Target(q, iff, "then"));
            QuestGraph.Detach(q, iff);
            Assert.AreEqual(0, QuestGraph.Root(q).Count);
            // Delete closes the gap; a block's steps go loose.
            QuestGraph.Connect(q, null, QuestGraph.Start, a);
            QuestGraph.Connect(q, a, "next", iff);
            QuestGraph.Connect(q, iff, "then", c);
            int loose = QuestGraph.Loose(q).Count;
            QuestGraph.Delete(q, iff);
            Assert.AreEqual(a, QuestGraph.Root(q).Last());
            Assert.AreEqual(loose + 1, QuestGraph.Loose(q).Count);
            Assert.IsNull(QuestGraph.Incoming(c).port);
            // Loose chains never run.
            StringAssert.DoesNotContain("title @a", QuestFile.Compile(q, new List<string>()));
        }

        [Test]
        public void ValueNodesBecomeTheExpression()
        {
            var iff = QuestSteps.New("if");
            var title = new JObject { ["type"] = "title", ["title"] = "Ace" };
            ((JArray)iff["then"]).Add(title);
            var q = Quest(iff);
            var kills = QuestGraph.NewValue(q, "state");
            kills["what"] = "kills";
            var cmp = QuestGraph.NewValue(q, "compare");
            cmp["op"] = ">=";
            cmp["a"] = QuestGraph.Wire((string)kills["id"]);
            cmp["b"] = 5;
            iff["condition"] = QuestGraph.Wire((string)cmp["id"]);
            var orbit = QuestGraph.NewValue(q, "orbit");
            orbit["station"] = 78;
            var players = QuestGraph.NewValue(q, "players");
            players["orbit"] = QuestGraph.Wire((string)orbit["id"]);
            title["who"] = QuestGraph.Wire((string)players["id"]);
            var problems = new List<string>();
            string script = QuestFile.Compile(q, problems);
            CollectionAssert.IsEmpty(problems);
            var lines = Lines(script);
            CollectionAssert.Contains(lines, "if (kills >= 5)");
            CollectionAssert.Contains(lines, "title @alive[orbit=78] Ace");
            Assert.IsNull(RunnerError(script), script);
            // A node that feeds itself is a problem, not a hang.
            cmp["b"] = QuestGraph.Wire((string)cmp["id"]);
            problems.Clear();
            QuestFile.Compile(q, problems);
            Assert.IsNotEmpty(problems);
            // Deleting a value node cuts its wires.
            QuestGraph.DeleteValue(q, cmp);
            Assert.IsNull(iff["condition"]);
        }

        [Test]
        public void VariablesStayInTexts()
        {
            var q = Quest(new JObject { ["type"] = "title", ["title"] = "Wave {wave} of {waves}, {nope}" });
            q["variables"] = new JArray(new JObject { ["name"] = "waves", ["value"] = 3 }, new JObject { ["name"] = "wave", ["value"] = 1 });
            var lines = Lines(QuestFile.Compile(q, new List<string>()));
            CollectionAssert.Contains(lines, "set waves = 3");
            CollectionAssert.Contains(lines, "title @a Wave {wave} of {waves}, (nope)");
        }

        [Test]
        public void TranslationsAndTheBackdropAreFiles()
        {
            var p = ModProject.Create("Text test", "tests");
            var it = p.AddItem(46, "Lance");
            p.Save();
            var keys = p.TranslatableTexts().Select(t => t.key).ToList();
            CollectionAssert.Contains(keys, $"items.{it.LocalId}.name");
            p.SetText("de", $"items.{it.LocalId}.name", "Lanze");
            Assert.AreEqual("Lanze", p.Texts("de")[$"items.{it.LocalId}.name"]);
            Assert.IsTrue(File.Exists(Path.Combine(p.Folder, "text", "de.json")));
            p.SetText("de", $"items.{it.LocalId}.name", "");
            Assert.IsFalse(File.Exists(Path.Combine(p.Folder, "text", "de.json")));
            p.Backdrop = new JObject { ["planets"] = "star" };
            Assert.AreEqual("star", (string)p.Backdrop["planets"]);
            p.Backdrop = null;
            Assert.IsNull(p.Backdrop);
        }

        // Remake (a modder's suggestions): the hand-in, Take an item, Restrict travel "khador", a spawn's hull / guns and
        // Spawn an NPC ship type; each line comes back as its step.
        [Test]
        public void HandInTakeKhadorAndNpcTypeStepsComeBack()
        {
            var cargo = QuestSteps.New("cargo");
            cargo["item"] = 22; cargo["amount"] = 5; cargo["station"] = 78; cargo["take"] = true;
            var take = QuestSteps.New("take");
            take["item"] = 85; take["mounted"] = true;
            var some = QuestSteps.New("take");
            some["item"] = 22; some["amount"] = 3;
            var restrict = QuestSteps.New("restrict");
            restrict["what"] = "khador";
            var spawn = QuestSteps.New("spawn");
            spawn["ship"] = 2; spawn["hull"] = 2; spawn["guns"] = 1.5;
            var boss = QuestSteps.New("spawntype");
            boss["npc"] = "pirate_boss"; boss["name"] = "Red"; boss["at"] = "0 0 9000";
            var problems = new List<string>();
            string script = QuestFile.Compile(Quest(cargo, take, some, restrict, spawn, boss), problems);
            CollectionAssert.IsEmpty(problems, script);
            var lines = Lines(script);
            foreach (var l in new[] { "wait until cargo(22) >= 5 and docked(78) > 0", "take @a 22 5", "take @a 85 all mounted", "take @a 22 3",
                                      "restrict @a khador", "spawn @a 2 enemy hull 2 guns 1.5", "spawn @a type pirate_boss named Red at 0 0 9000" })
                CollectionAssert.Contains(lines, l, script);
            Assert.IsNull(RunnerError(script), script);

            var back = ((JArray)QuestFile.FromScript(script, "T", new List<string>())["steps"]).OfType<JObject>().ToList();
            var takes = back.Where(s => (string)s["type"] == "take").ToList();
            Assert.AreEqual(3, takes.Count, string.Join("\n", back));
            Assert.AreEqual("85", takes[1]["item"].ToString());
            Assert.IsNull(takes[1]["amount"], "all");
            Assert.IsTrue((bool)takes[1]["mounted"]);
            Assert.AreEqual(3.0, (double)takes[2]["amount"]);
            Assert.AreEqual("khador", (string)back.Single(s => (string)s["type"] == "restrict")["what"]);
            var sp = back.Single(s => (string)s["type"] == "spawn");
            Assert.AreEqual(2.0, (double)sp["hull"]);
            Assert.AreEqual(1.5, (double)sp["guns"]);
            Assert.AreEqual("2", sp["ship"].ToString());
            var bt = back.Single(s => (string)s["type"] == "spawntype");
            Assert.AreEqual("pirate_boss", (string)bt["npc"]);
            Assert.AreEqual("Red", (string)bt["name"]);
            Assert.AreEqual("0 0 9000", (string)bt["at"]);
        }

        // Remake: "When something happens" on a station's pad outside (stations.json "orbit.pads").
        [Test]
        public void APadTriggerCompiles()
        {
            var on = QuestSteps.New("on");
            on["trigger"] = "pad";
            on["station"] = 78;
            on["steps"] = new JArray(QuestSteps.New("heal"));
            var problems = new List<string>();
            string script = QuestFile.Compile(Quest(on), problems);
            CollectionAssert.IsEmpty(problems, script);
            CollectionAssert.Contains(Lines(script), "on pad 78", script);
            Assert.IsNull(RunnerError(script), script);
        }
    }
}
