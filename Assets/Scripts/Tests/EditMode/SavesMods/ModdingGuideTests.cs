// ModdingGuideTests.cs
// Feature: CLAUDE.md "Mods" > Mod Builder MCP server's read_guide (Modding.ModdingGuide, UI.ModBuilderMcpGuide): the AI
// modding guide served to a connected assistant. Every file it names is in the repository (Modding/ai/gof2-modding, which a
// build copies in: EditorTools.ModdingGuideBundle); a file's headings skip code blocks; a section runs to the next heading of
// its level or above (a heading's words find it); a search needs every word on one line and names the heading above it; a
// range is cut at MaxChars saying where it goes on; read_guide lists the files, outlines a long file and refuses an unknown
// one; resources/list and resources/read hand out the same files (gof2-guide://<file>), an unknown uri is an error.

using System.IO;
using System.Linq;
using System.Reflection;
using GoF2Remake.Modding;
using GoF2Remake.UI;
using Newtonsoft.Json.Linq;
using NUnit.Framework;

namespace GoF2Remake.Tests
{
    public class ModdingGuideTests
    {
        string folder;

        const string Skill = "# The skill\n\nIntro line.\n\n## Ships\n\nA ship has armor.\n\n```\n# not a heading\n```\n\n### Mounts\n\nglowShape square here.\n\n" +
                             "## Items\n\nAn item has a price.\n";

        [SetUp]
        public void SetUp()
        {
            folder = Path.Combine(Path.GetTempPath(), "gof2_guide_test_" + System.Guid.NewGuid().ToString("N"));
            Directory.CreateDirectory(folder);
            File.WriteAllText(Path.Combine(folder, "SKILL.md"), Skill.Replace("\n", "\r\n"));
            File.WriteAllText(Path.Combine(folder, "reference.md"), string.Join("\n", Enumerable.Range(1, 3000).Select(i => $"row {i} " + new string('x', 40))));
            ModdingGuide.FolderOverride = folder;
            ModdingGuide.Forget();
        }

        [TearDown]
        public void TearDown()
        {
            ModdingGuide.FolderOverride = null;
            ModdingGuide.Forget();
            Directory.Delete(folder, true);
        }

        [Test]
        public void EveryGuideFileIsInTheRepository()
        {
            ModdingGuide.FolderOverride = null;
            ModdingGuide.Forget();
            foreach (var (file, _) in ModdingGuide.Files)
                Assert.IsTrue(ModdingGuide.Has(file), $"{ModdingGuide.RepositoryFolder}/{file} missing");
            Assert.Greater(ModdingGuide.Headings("SKILL.md").Count, 5);
            Assert.IsNotNull(ModdingGuide.Section("guide.md", "Ships", out _));
        }

        [Test]
        public void HeadingsSectionsAndSearch()
        {
            var h = ModdingGuide.Headings("SKILL.md");
            CollectionAssert.AreEqual(new[] { "The skill", "Ships", "Mounts", "Items" }, h.Select(x => x.title).ToArray(), "a # in a code block isn't a heading");
            string ships = ModdingGuide.Section("SKILL.md", "ships", out int from);
            Assert.AreEqual(5, from);
            StringAssert.Contains("glowShape square", ships, "a subsection belongs to its section");
            StringAssert.DoesNotContain("price", ships, "the next heading of its level ends it");
            StringAssert.Contains("price", ModdingGuide.Section("SKILL.md", "item", out _), "a heading's words find it");
            Assert.IsNull(ModdingGuide.Section("SKILL.md", "Planets", out _));

            var hits = ModdingGuide.Search("SQUARE glowshape");
            Assert.AreEqual(1, hits.Count);
            Assert.AreEqual(("SKILL.md", 15, "Mounts"), (hits[0].file, hits[0].line, hits[0].heading));
            Assert.AreEqual(0, ModdingGuide.Search("square price").Count, "every word on one line");
            Assert.AreEqual("SKILL.md", ModdingGuide.Find("skill"));
            Assert.IsNull(ModdingGuide.Find("secrets.md"));
        }

        [Test]
        public void ALongRangeIsCutSayingWhereItGoesOn()
        {
            string r = ModdingGuide.Range("reference.md", 1, 3000);
            Assert.LessOrEqual(r.Length, ModdingGuide.MaxChars + 100);
            StringAssert.Contains("it goes on at line", r);
            StringAssert.StartsWith("row 10 ", ModdingGuide.Range("reference.md", 10, 2));
        }

        static string RunReadGuide(JObject args)
        {
            var tools = (System.Collections.IEnumerable)typeof(ModBuilderMcp).GetProperty("Tools", BindingFlags.NonPublic | BindingFlags.Static).GetValue(null);
            var tool = tools.Cast<ModBuilderMcp.Tool>().First(t => t.name == "read_guide");
            Assert.IsTrue(tool.readOnly);
            var job = new ModBuilderMcp.Job { id = 1, tool = tool, args = args };
            tool.run(args, new ModBuilderMcp.Call { job = job });
            Assert.IsNotNull(job.response, "answered at once");
            var result = job.response["result"];
            return ((bool)result["isError"] ? "ERROR " : "") + (string)result["content"][0]["text"];
        }

        [Test]
        public void ReadGuideListsOutlinesAndReads()
        {
            StringAssert.Contains("SKILL.md", RunReadGuide(new JObject()));
            StringAssert.Contains("not in this game", RunReadGuide(new JObject()), "a file the folder lacks");
            StringAssert.Contains("Intro line.", RunReadGuide(new JObject { ["file"] = "SKILL.md" }), "a short file whole");
            StringAssert.Contains("is long", RunReadGuide(new JObject { ["file"] = "reference.md" }), "a long one outlined");
            StringAssert.Contains("glowShape square", RunReadGuide(new JObject { ["file"] = "SKILL.md", ["section"] = "Mounts" }));
            StringAssert.Contains("SKILL.md:15 [Mounts]", RunReadGuide(new JObject { ["search"] = "glowShape" }));
            StringAssert.StartsWith("ERROR", RunReadGuide(new JObject { ["file"] = "../secrets.txt" }));
            StringAssert.StartsWith("ERROR", RunReadGuide(new JObject { ["file"] = "voices.md" }), "not in this game");
        }

        static JObject Dispatch(JObject message)
        {
            var m = typeof(ModBuilderMcp).GetMethod("Dispatch", BindingFlags.NonPublic | BindingFlags.Static);
            return (JObject)m.Invoke(null, new object[] { message, null });
        }

        static JObject Rpc(string method, JObject parameters = null) =>
            new JObject { ["jsonrpc"] = "2.0", ["method"] = method, ["params"] = parameters ?? new JObject(), ["id"] = 1 };

        [Test]
        public void ResourcesHandOutTheGuide()
        {
            ModdingGuide.Preload();
            Assert.IsNotNull(Dispatch(Rpc("initialize"))["result"]["capabilities"]["resources"]);
            var list = ((JArray)Dispatch(Rpc("resources/list"))["result"]["resources"]).OfType<JObject>().ToList();
            CollectionAssert.AreEqual(new[] { "gof2-guide://SKILL.md", "gof2-guide://reference.md" }, list.Select(r => (string)r["uri"]).ToArray());
            var read = Dispatch(Rpc("resources/read", new JObject { ["uri"] = "gof2-guide://SKILL.md" }))["result"]["contents"][0];
            Assert.AreEqual("text/markdown", (string)read["mimeType"]);
            StringAssert.Contains("## Items", (string)read["text"]);
            Assert.AreEqual(-32002, (int)Dispatch(Rpc("resources/read", new JObject { ["uri"] = "gof2-guide://../x" }))["error"]["code"]);
        }
    }
}
