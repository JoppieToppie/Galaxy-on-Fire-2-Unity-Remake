// EventGraphFilesTests.cs
// Feature: CLAUDE.md "Multiplayer" > Event graphs / Events: the game reads every .gof2event itself (EventGraphFile, Graph
// Toolkit's YAML) and compiles it to the runner's line form (EventGraphScript); the built-ins (Resources/GoF2Events) and
// the Editor's templates (Scripts/Editor/Events/Templates) must read and compile without errors, and the runner must
// accept what the compiler writes (EventRunner.Check).
// Expected values: the node kinds each file holds (counted in the files) and the line each kind compiles to (the
// compiler's node table, EventGraphScript.Emit: Spawn -> "spawn", Ask -> "ask", Free For All -> "pvp", ...).

using System.Collections.Generic;
using System.IO;
using System.Linq;
using GoF2Remake.Events;
using NUnit.Framework;
using UnityEngine;

namespace GoF2Remake.Tests
{
    [Unity.Scripting.LifecycleManagement.NoAutoStaticsCleanup]
    public class EventGraphFilesTests : SandboxTest
    {
        static string BuiltInFolder => Path.Combine(Application.dataPath, "Resources", "GoF2Events");
        static string TemplateFolder => Path.Combine(Application.dataPath, "Scripts", "Editor", "Events", "Templates");

        static IEnumerable<string> AllGraphFiles()
        {
            foreach (var dir in new[] { BuiltInFolder, TemplateFolder })
                if (Directory.Exists(dir))
                    foreach (var f in Directory.GetFiles(dir, "*." + EventRunner.GraphExtension)) yield return f;
        }

        static string Compile(string path, out List<EventGraphScript.Problem> problems)
        {
            problems = new List<EventGraphScript.Problem>();
            return EventGraphScript.FromFile(File.ReadAllText(path), problems);
        }

        [Test]
        public void TheBuiltInsAndTemplatesExist()
        {
            var names = AllGraphFiles().Select(f => Path.GetFileNameWithoutExtension(f)).ToList();
            foreach (var n in new[] { "waves", "survival", "pirate_hideout" })
                Assert.AreEqual(2, names.Count(x => x == n), n + ": a built-in and a template");
            foreach (var n in new[] { "boss_fight", "free_for_all", "king_of_the_hill", "pirate_base", "quiz", "race", "vote" })
                Assert.Contains(n, names, "template " + n);
        }

        [Test]
        public void EveryGraphFileIsAGraph()
        {
            foreach (var f in AllGraphFiles())
                Assert.IsTrue(EventGraphScript.IsGraph(File.ReadAllText(f)), f);
            Assert.IsFalse(EventGraphScript.IsGraph("wait 5\nheal @a"));
            Assert.IsFalse(EventGraphScript.IsGraph(null));
        }

        [Test]
        public void EveryGraphFileReadsWithOneStartLeadingSomewhere()
        {
            foreach (var f in AllGraphFiles())
            {
                var data = EventGraphFile.Read(File.ReadAllText(f));
                Assert.IsNotNull(data, f);
                Assert.Greater(data.nodes.Count, 1, f);
                Assert.Greater(data.wires.Count, 0, f);
                var main = data.nodes.Where(n => data.mainGraph == null || n.graph == null || n.graph == data.mainGraph).ToList();
                Assert.AreEqual(1, main.Count(n => n.kind == "EventStartNode"), f + ": one Start node in the main graph");
                var ids = new HashSet<string>(data.nodes.Select(n => n.id));
                Assert.AreEqual(data.nodes.Count, ids.Count, f + ": node ids are unique");
                var start = main.Single(n => n.kind == "EventStartNode");
                Assert.IsTrue(data.wires.Any(w => w.fromNode == start.id && ids.Contains(w.toNode)), f + ": the flow leaves Start");
            }
        }

        [Test]
        public void EveryGraphFileCompilesWithoutErrors()
        {
            foreach (var f in AllGraphFiles())
            {
                string script = Compile(f, out var problems);
                var errors = EventsTestUtil.Errors(problems);
                Assert.IsEmpty(errors, Path.GetFileName(f) + ": " + EventsTestUtil.Describe(errors));
                Assert.IsNotEmpty(EventsTestUtil.Lines(script), f);
            }
        }

        [Test]
        public void TheRunnerAcceptsEveryCompiledGraph()
        {
            foreach (var f in AllGraphFiles())
            {
                string script = Compile(f, out _);
                Assert.IsNull(EventRunner.Check(script), Path.GetFileName(f));
            }
        }

        [Test]
        public void EveryCompiledStepNamesItsNode()
        {
            // Every line a node writes ends in "#@<node id>" (the Editor's live view), the id of a node in the file.
            foreach (var f in AllGraphFiles())
            {
                string text = File.ReadAllText(f);
                var ids = new HashSet<string>(EventGraphFile.Read(text).nodes.Select(n => n.id));
                string script = Compile(f, out _);
                int marked = 0;
                foreach (string raw in script.Split('\n'))
                {
                    int at = raw.IndexOf("#@", System.StringComparison.Ordinal);
                    if (at < 0) continue;
                    marked++;
                    Assert.IsTrue(ids.Contains(raw.Substring(at + 2).Trim()), f + ": " + raw);
                }
                Assert.Greater(marked, 0, f);
            }
        }

        // The line kinds each graph compiles to (its nodes: counted in the files).
        static readonly (string file, string[] words)[] Expected =
        {
            ("pirate_hideout", new[] { "mission", "spawn", "waypoint", "radio", "wait", "if", "complete", "fail", "title" }),
            ("survival", new[] { "wait", "title", "while", "if", "winner", "timer", "stop", "spawn", "set", "score", "reward", "heal" }),
            ("waves", new[] { "repeat", "wait", "spawn", "reward", "heal", "ammo", "score", "winner", "timer", "stop", "title", "if" }),
            ("boss_fight", new[] { "on", "sound", "music", "scoreboard", "spawn", "winner", "score", "reward", "title" }),
            ("free_for_all", new[] { "on", "pvp", "respawn", "restrict", "points", "tp", "music", "scoreboard", "timer" }),
            ("king_of_the_hill", new[] { "every", "on", "points", "spawn", "scoreboard", "music", "sound" }),
            ("pirate_base", new[] { "radio", "provoke", "ask", "waypoint", "spawn", "music", "tp", "reward", "on" }),
            ("quiz", new[] { "ask", "points", "sound", "scoreboard", "winner", "score" }),
            ("race", new[] { "on", "timer", "sound", "reward", "stop" }),
            ("vote", new[] { "vote", "startevent", "title" }),
        };

        [Test]
        public void TheGraphsCompileToTheirLineKinds()
        {
            foreach (var (file, words) in Expected)
            {
                string path = Path.Combine(file == "pirate_hideout" || file == "survival" || file == "waves" ? BuiltInFolder : TemplateFolder,
                                           file + "." + EventRunner.GraphExtension);
                Assert.IsTrue(File.Exists(path), path);
                var have = EventsTestUtil.Words(Compile(path, out _));
                foreach (var w in words) Assert.IsTrue(have.Contains(w), $"{file}: no \"{w}\" line (has {string.Join(", ", have)})");
            }
        }

        [Test]
        public void ThePirateHideoutIsABarMission()
        {
            // CLAUDE.md: Pirate Hideout is a bar mission: its Start node's Mission settings become "mission" lines.
            string script = Compile(Path.Combine(BuiltInFolder, "pirate_hideout." + EventRunner.GraphExtension), out _);
            var lines = EventsTestUtil.Lines(script);
            Assert.IsTrue(lines.Any(l => l.StartsWith("mission title ")), "a mission title");
            Assert.IsTrue(lines.Any(l => l.StartsWith("mission players ")), "the pilot counts");
            Assert.IsFalse(lines.Any(l => l == "mission kind quest"), "not a quest");
        }

        [Test]
        public void TheEventsAreNotQuests()
        {
            foreach (var f in AllGraphFiles())
                Assert.IsFalse(EventsTestUtil.Lines(Compile(f, out _)).Contains("mission kind quest"), f);
        }

        [Test]
        public void ABrokenFileIsAProblemNotAnException()
        {
            var problems = new List<EventGraphScript.Problem>();
            string script = EventGraphScript.FromFile("%YAML 1.1\nGraphModelImp\n  - [unclosed", problems);
            Assert.IsNotNull(script);
            Assert.IsNotEmpty(EventsTestUtil.Errors(problems));
        }

        [Test]
        public void TheGraphListIsReadAgainOnlyWhenAnEventsFolderFileChanges()
        {
            // #275: every 30 s AllGraphs read and compiled every graph again (a 0.75 s stall on an iPhone); now it looks at
            // the Events folders' files and compiles only when one was added, changed or removed.
            string dir = Path.Combine(GoF2Remake.GamePaths.Data, "Events");
            string added = Path.Combine(dir, "zz_added_graph." + EventRunner.GraphExtension);
            try
            {
                EventRunner.ExpireGraphCheck(forget: true);
                EventRunner.AllGraphs();
                int compiles = EventRunner.GraphCompiles;
                EventRunner.ExpireGraphCheck();
                EventRunner.AllGraphs();
                Assert.AreEqual(compiles, EventRunner.GraphCompiles, "30 s on with no file changed: the same list");

                Directory.CreateDirectory(dir);
                File.Copy(Path.Combine(BuiltInFolder, "waves." + EventRunner.GraphExtension), added);
                Assert.IsFalse(EventRunner.AllGraphs().Exists(g => g.name == "zz_added_graph"), "not looked within the 30 s");
                EventRunner.ExpireGraphCheck();
                Assert.IsTrue(EventRunner.AllGraphs().Exists(g => g.name == "zz_added_graph"), "a file added by hand is found");
                Assert.AreEqual(compiles + 1, EventRunner.GraphCompiles);
            }
            finally
            {
                if (File.Exists(added)) File.Delete(added);
                EventRunner.ExpireGraphCheck(forget: true);
            }
        }
    }
}
