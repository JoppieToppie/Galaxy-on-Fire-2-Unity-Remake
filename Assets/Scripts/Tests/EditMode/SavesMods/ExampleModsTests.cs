// ExampleModsTests.cs
// Every mod in Modding/Examples loads cleanly (docs/notes/mods.md "The examples"): with its dependencies on, its JSON,
// models' references, event graphs and scripts give no warning, its scripts start without an error, and the game's tables
// load with it. The examples teach the format, so a field the loader doesn't know (a typo, a renamed field) or a graph that
// doesn't compile must fail here, not in a modder's copy. Each example's own behaviour has its own tests.

using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using GoF2Remake.Data;
using GoF2Remake.Events;
using GoF2Remake.Modding;
using GoF2Remake.Patching;
using NUnit.Framework;

namespace GoF2Remake.Tests
{
    public class ExampleModsTests : SavesModsTest
    {
        static string ExamplesFolder => RepoPath("Modding", "Examples");

        static IEnumerable<string> Examples()
        {
            string dir = RepoPath("Modding", "Examples");
            if (dir == null) return new[] { "(none)" };
            return Directory.GetDirectories(dir).Select(Path.GetFileName).OrderBy(n => n, StringComparer.Ordinal);
        }

        [SetUp]
        public void OwnApprovals()
        {
            ModScripts.StopAll();
            ModScripts.ClearLines();
            ModScripts.ApprovedOverride = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
            PatchRuntime.RemoveAll();
        }

        [TearDown]
        public void StopScripts()
        {
            ModScripts.StopAll();
            ModScripts.ClearLines();
            ModScripts.ApprovedOverride = null;
            PatchRuntime.RemoveAll();
        }

        [TestCaseSource(nameof(Examples))]
        public void LoadsWithoutAWarning(string name)
        {
            if (ExamplesFolder == null) Assert.Ignore("Modding/Examples isn't in this checkout");
            foreach (string other in Directory.GetDirectories(ExamplesFolder)) CopyExample(Path.GetFileName(other));   // (its dependencies too)
            ModManager.Scan();
            var mod = Installed(name);
            var on = new List<ModInfo> { mod };
            for (int i = 0; i < on.Count; i++)
                foreach (string dep in on[i].Manifest.dependencies)
                {
                    string id = dep.Split('>', '=', '<')[0].Trim();
                    var d = ModManager.Find(id);
                    Assert.IsNotNull(d, $"{name} needs {id}, which isn't among the examples");
                    if (!on.Contains(d)) on.Add(d);
                }
            foreach (var m in on) ModManager.SetEnabled(m.Id, true);
            foreach (var m in on) ModScripts.SetApproved(m, true);

            var db = Database.Load();
            ModScripts.Sync(true);
            Assert.IsNotNull(db);
            foreach (string graph in mod.Source.FilesIn("events", "." + EventRunner.GraphExtension, "." + QuestFile.Extension))
            {
                string text = mod.Source.ReadText(graph);
                string problem = EventRunner.CheckScript(QuestFile.Is(text) ? QuestFile.Compile(text, new List<string>()) : EventGraphScript.FromFile(text));
                Assert.IsNull(problem, $"{graph}: {problem}");
                if (QuestFile.Is(text))
                {
                    var issues = new List<string>();
                    QuestFile.Compile(text, issues);
                    Assert.IsEmpty(issues, graph + ": " + string.Join("; ", issues));
                }
                else
                {
                    var problems = new List<EventGraphScript.Problem>();
                    EventGraphScript.FromFile(text, problems);
                    Assert.IsEmpty(problems, graph + ": " + string.Join("; ", problems.Select(p => (p.node ?? "graph") + ": " + p.message)));
                }
            }
            foreach (var m in on)
            {
                Assert.IsFalse(m.Broken, AllMessages(m));
                Assert.IsEmpty(m.Warnings, m.Id + ": " + AllMessages(m));
            }
            Assert.AreEqual("", string.Join("|", ModScripts.Lines.Where(l => l.error).Select(l => l.mod + ": " + l.text)));
        }

        [Test]
        public void EveryExampleHasADescriptionAndATest()
        {
            if (ExamplesFolder == null) Assert.Ignore("Modding/Examples isn't in this checkout");
            string tests = RepoPath("Assets", "Scripts", "Tests");
            string all = string.Join("\n", Directory.GetFiles(tests, "*.cs", SearchOption.AllDirectories).Select(File.ReadAllText));
            foreach (string dir in Directory.GetDirectories(ExamplesFolder))
            {
                string name = Path.GetFileName(dir);
                var manifest = File.ReadAllText(Path.Combine(dir, "mod.json"));
                StringAssert.Contains("\"description\"", manifest, name);
                StringAssert.Contains("\"" + name + "\"", all, name + ": no test names it beyond loading (ExampleModsTests)");
            }
        }
    }
}
