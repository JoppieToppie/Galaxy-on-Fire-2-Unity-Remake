// The pause menu's "Report an issue" (UI.IssueReport, UI.IssueReportUpload): what a report carries, its zip in the data folder's
// IssueReports, the kept log lines, the home folder cut from its texts, the filled-in GitHub issue's address, the upload's
// size limits. And the Editor's UiPanelHangGuard: a half-inserted UI element is taken out of its parent's child list (what
// hung the Editor after a code reload, see its header).

using System;
using System.Collections.Generic;
using System.IO;
using System.IO.Compression;
using System.Linq;
using System.Reflection;
using System.Text;
using GoF2Remake.Data;
using GoF2Remake.UI;
using NUnit.Framework;
using UnityEngine;
using UnityEngine.UIElements;

namespace GoF2Remake.Tests
{
    public class IssueReportTests : SandboxTest
    {
        static readonly DateTime When = new DateTime(2026, 11, 13, 17, 5, 9);

        static string TextOf(List<IssueReport.Entry> files, string name)
        {
            var f = files.Find(e => e.name == name);
            return f == null ? null : Encoding.UTF8.GetString(f.data);
        }

        [Test]
        public void TheTitleIsTheFirstLine()
        {
            Assert.AreEqual("The jumpgate ate my ship", IssueReport.Title("\n  The jumpgate ate my ship  \nIt happened at Var Hastra."));
            Assert.AreEqual("Issue report", IssueReport.Title("  \n "));
            Assert.AreEqual("Issue report", IssueReport.Title(null));
            string title = IssueReport.Title(new string('x', 300));
            Assert.AreEqual(80, title.Length);
            StringAssert.EndsWith("...", title);
        }

        [Test]
        public void TheHomeFolderIsCutFromTexts()
        {
            const string home = @"C:\Users\somebody";
            Assert.AreEqual(@"saved to ~\AppData\x.log", IssueReport.Scrub(@"saved to C:\Users\somebody\AppData\x.log", home));
            Assert.AreEqual("at ~/AppData/x.log and ~/y", IssueReport.Scrub("at C:/Users/somebody/AppData/x.log and c:/users/SOMEBODY/y", home), "either slash, any case");
            Assert.AreEqual("nothing here", IssueReport.Scrub("nothing here", home));
            Assert.AreEqual("kept", IssueReport.Scrub("kept", ""), "no home folder known: the text as it is");
            Assert.AreEqual("", IssueReport.Scrub(null, home));
        }

        [Test]
        public void TheLogKeepsItsLastLines()
        {
            for (int i = 0; i < IssueReport.LogEntries + 50; i++) IssueReport.AddLog("line " + i, "at Somewhere()", LogType.Log, When);
            IssueReport.AddLog("it broke", "at Game.Break()\nat Game.Run()", LogType.Error, When);
            string text = IssueReport.LogText();
            StringAssert.DoesNotContain("] line 50\n", text, "the oldest lines are gone");
            StringAssert.Contains("] line " + (IssueReport.LogEntries + 49) + "\n", text);
            StringAssert.Contains("17:05:09.000 [Error] it broke\n    at Game.Break()\n    at Game.Run()\n", text, "an error keeps its stack");
            StringAssert.DoesNotContain("at Somewhere()", text, "a plain line's stack is left out");
            Assert.Less(text.Split('\n').Count(l => l.Contains("] line ")), IssueReport.LogEntries);
        }

        [Test]
        public void AReportCarriesItsFiles()
        {
            var draft = new IssueReport.Draft { description = "The jumpgate ate my ship\nat Var Hastra", screenshot = new byte[] { 1, 2, 3 }, context = "Orbit: Kaamo (Aquila)\n" };
            var files = IssueReport.Collect(draft, When);
            CollectionAssert.AreEqual(new[] { "report.txt", "screenshot.jpg", "log.txt", "settings.txt", "save.json" }, files.Select(f => f.name).ToArray(),
                                      "the Editor's own log file is left out");
            string report = TextOf(files, "report.txt");
            StringAssert.Contains("Time: 2026-11-13 17:05:09", report);
            StringAssert.Contains("---- What happened ----\nThe jumpgate ate my ship\nat Var Hastra\n", report);
            StringAssert.Contains("Version: " + BuildVersion.Text, report);
            StringAssert.Contains("GPU: ", report);
            StringAssert.Contains("Campaign: ", report);
            StringAssert.Contains("Orbit: Kaamo (Aquila)", report);
            StringAssert.Contains("Active (in load order): 0", report, "the sandbox has no mods");
            StringAssert.DoesNotContain(SystemInfo.deviceUniqueIdentifier, report);
            StringAssert.Contains("Sound / master = ", TextOf(files, "settings.txt"));

            Assert.IsTrue(SaveGame.TryParse(TextOf(files, "save.json"), Db, out var save, out string problem), problem);
            Assert.AreEqual(Session.StationIndex, save.station, "the running game, loadable as a save");

            draft.includeSave = false;
            draft.screenshot = null;
            files = IssueReport.Collect(draft, When);
            Assert.IsNull(files.Find(f => f.name == "save.json"));
            Assert.IsNull(files.Find(f => f.name == "screenshot.jpg"));
        }

        [Test]
        public void TheZipGoesIntoTheDataFolder()
        {
            var files = IssueReport.Collect(new IssueReport.Draft { description = "x", screenshot = new byte[] { 9, 8, 7 } }, When);
            string path = IssueReport.WriteZip(files, When);
            Assert.AreEqual(Path.Combine(Sandbox, "IssueReports", "GoF2_issue_20261113_170509.zip"), path);
            using (var zip = ZipFile.OpenRead(path))
            {
                CollectionAssert.AreEquivalent(files.Select(f => f.name).ToArray(), zip.Entries.Select(e => e.FullName).ToArray());
                using var s = zip.GetEntry("screenshot.jpg").Open();
                var bytes = new byte[8];
                Assert.AreEqual(3, s.Read(bytes, 0, 8));
                Assert.AreEqual(7, bytes[2]);
            }
            string second = IssueReport.WriteZip(files, When);
            Assert.AreEqual(Path.Combine(Sandbox, "IssueReports", "GoF2_issue_20261113_170509_2.zip"), second, "a second report in the same second keeps the first");
            Assert.IsNull(IssueReport.WriteZip(new List<IssueReport.Entry>(), When));
        }

        [Test]
        public void TheFolderKeepsItsNewestReports()
        {
            var files = new List<IssueReport.Entry> { new IssueReport.Entry { name = "report.txt", mediaType = "text/plain", data = new byte[] { 65 } } };
            for (int i = 0; i < IssueReport.KeepReports + 5; i++) IssueReport.WriteZip(files, When.AddMinutes(i));
            var left = Directory.GetFiles(IssueReport.Folder, "*.zip").Select(Path.GetFileName).OrderBy(n => n, StringComparer.Ordinal).ToList();
            Assert.AreEqual(IssueReport.KeepReports, left.Count);
            Assert.AreEqual($"GoF2_issue_{When.AddMinutes(5):yyyyMMdd_HHmmss}.zip", left[0], "the five oldest went");
        }

        [Test]
        public void TheGitHubIssueIsFilledIn()
        {
            string url = IssueReport.IssueUrl("Crash & burn?\nSecond line", "GoF2_issue_20261113_170509.zip");
            StringAssert.StartsWith("https://github.com/" + UpdateCheck.Repository + "/issues/new?title=Crash%20%26%20burn%3F&body=", url);
            StringAssert.Contains(Uri.EscapeDataString("Please attach the report file here: GoF2_issue_20261113_170509.zip"), url);
            StringAssert.Contains(Uri.EscapeDataString("Version: " + BuildVersion.Text), url);
            string longest = IssueReport.IssueUrl(new string('é', IssueReport.MaxDescription), "x.zip");
            Assert.Less(longest.Length, 8000, "GitHub refuses a longer address");
        }

        [Test]
        public void AnUploadLeavesOutWhatIsTooBig()
        {
            IssueReport.Entry File(string name, int size) => new IssueReport.Entry { name = name, data = new byte[size] };
            var fit = IssueReportUpload.Fitting(new List<IssueReport.Entry>
            {
                File("report.txt", 1000), File("huge.log", IssueReportUpload.MaxAttachment + 1), File("a", IssueReportUpload.MaxAttachment),
                File("b", IssueReportUpload.MaxAttachment), File("c", IssueReportUpload.MaxAttachment), File("small", 10), null,
            });
            CollectionAssert.AreEqual(new[] { "report.txt", "a", "b", "small" }, fit.Select(f => f.name).ToArray(), "one file's limit, then the report's");
            Assert.IsEmpty(IssueReportUpload.Fitting(null));
        }

        [Test]
        public void TheEditorGuardTakesAHalfInsertedElementOut()
        {
            // What an exception in the middle of VisualElement.Hierarchy.Insert leaves: the child in the parent's list, the
            // child's parent unset. UI Toolkit's style pass never gets past such a child (the Editor hung there).
            var guard = Type.GetType("GoF2Remake.EditorTools.UiPanelHangGuard, Assembly-CSharp-Editor");
            Assert.IsNotNull(guard, "Assets/Scripts/Editor/UiPanelHangGuard.cs");
            var removeOrphans = guard.GetMethod("RemoveOrphans", BindingFlags.Public | BindingFlags.Static);
            var childrenField = typeof(VisualElement).GetField("m_Children", BindingFlags.NonPublic | BindingFlags.Instance);
            Assert.IsNotNull(childrenField, "VisualElement.m_Children: the guard reaches it by name");

            var root = new VisualElement();
            var branch = new VisualElement();
            var leaf = new VisualElement();
            root.Add(branch);
            branch.Add(leaf);
            var orphan = new VisualElement();
            ((List<VisualElement>)childrenField.GetValue(branch)).Add(orphan);
            Assert.AreEqual(2, branch.hierarchy.childCount);
            Assert.IsNull(orphan.hierarchy.parent);

            var taken = new List<VisualElement>();
            Assert.AreEqual(1, removeOrphans.Invoke(null, new object[] { root, taken }));
            CollectionAssert.AreEqual(new[] { orphan }, taken);
            Assert.AreEqual(1, branch.hierarchy.childCount);
            Assert.AreSame(leaf, branch.hierarchy[0]);
            Assert.AreEqual(0, removeOrphans.Invoke(null, new object[] { root, taken }), "a sound tree is left alone");
        }
    }
}
