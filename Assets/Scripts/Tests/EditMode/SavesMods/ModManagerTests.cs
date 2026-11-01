// ModManagerTests.cs
// Feature: CLAUDE.md "Mods (remake-only)" > On / off and Dependencies (ModManager.cs): the Mods folders are scanned for
// folders and zips with a mod.json (a test sandbox reads only its own Mods folder); new mods start off; the enabled list is
// PlayerPrefs "mods_enabled" (ids in load order, one per line); Active = the enabled, unbroken mods whose dependencies are
// on and new enough, dependencies first; turning a mod on turns on what it needs, turning one off turns off what needs it;
// a broken mod.json or a duplicate id makes a mod unusable; a multiplayer session runs exactly the host's mods
// (BeginSession) and numbers their content afresh without touching the registry; every change bumps Revision.

using System.Linq;
using GoF2Remake.Modding;
using NUnit.Framework;
using UnityEngine;

namespace GoF2Remake.Tests
{
    public class ModManagerTests : SavesModsTest
    {
        static string[] ActiveIds() => ModManager.UserMods.Select(m => m.Id).ToArray();

        [Test]
        public void TheSandboxReadsOnlyItsOwnModsFolder()
        {
            CollectionAssert.AreEqual(new[] { System.IO.Path.Combine(Sandbox, "Mods") }, ModManager.Folders.ToArray());
            Assert.AreEqual(System.IO.Path.Combine(Sandbox, "Mods"), ModManager.UserFolder);
            ModManager.Scan();
            Assert.AreEqual(0, ModManager.Installed.Count);
            Assert.AreEqual(0, ModManager.UserMods.Count);
            Assert.IsTrue(System.IO.Directory.Exists(ModsFolder), "the user folder is made on the first scan");
        }

        [Test]
        public void ScanFindsFolderAndZipMods()
        {
            WriteMod("alpha", "alpha_mod");
            WriteZip(System.IO.Path.Combine(ModsFolder, "beta.zip"), false, ("beta/mod.json", Bytes("{ 'id': 'beta_mod', 'name': 'Beta' }")));
            System.IO.Directory.CreateDirectory(System.IO.Path.Combine(ModsFolder, "not_a_mod"));
            WriteMod(".hidden", "hidden_mod");
            ModManager.Scan();
            CollectionAssert.AreEquivalent(new[] { "alpha_mod", "beta_mod" }, ModManager.Installed.Select(m => m.Id).ToArray());
            Assert.IsInstanceOf<ZipSource>(Installed("beta_mod").Source);
            Assert.AreEqual("Beta", Installed("beta_mod").Name);
            Assert.AreSame(Installed("alpha_mod"), ModManager.Find("ALPHA_MOD"), "ids are found without case");
            Assert.IsNull(ModManager.Find("gamma"));
        }

        [Test]
        public void NewModsStartOff()
        {
            WriteMod("alpha", "alpha_mod");
            ModManager.Scan();
            Assert.IsFalse(ModManager.IsEnabled("alpha_mod"));
            Assert.AreEqual(0, ModManager.UserMods.Count);
        }

        [Test]
        public void TurningOnStoresTheIdsInPlayerPrefs()
        {
            WriteMod("a", "a_mod");
            WriteMod("b", "b_mod");
            ScanAndEnable("b_mod", "a_mod");
            Assert.AreEqual("b_mod\na_mod", PlayerPrefs.GetString("mods_enabled"));
            CollectionAssert.AreEqual(new[] { "b_mod", "a_mod" }, ActiveIds(), "the load order is the order they were turned on");
            ModManager.SetEnabled("b_mod", false);
            Assert.AreEqual("a_mod", PlayerPrefs.GetString("mods_enabled"));
            CollectionAssert.AreEqual(new[] { "a_mod" }, ActiveIds());
        }

        [Test]
        public void TheEnabledListIsReadFromPlayerPrefs()
        {
            WriteMod("a", "a_mod");
            WriteMod("b", "b_mod");
            PlayerPrefs.SetString("mods_enabled", " b_mod \n\na_mod\nb_mod\nmissing_mod");
            ModManager.Scan();
            Assert.IsTrue(ModManager.IsEnabled("a_mod"));
            Assert.IsTrue(ModManager.IsEnabled("b_mod"));
            CollectionAssert.AreEqual(new[] { "b_mod", "a_mod" }, ActiveIds(), "trimmed, duplicates and missing mods left out");
            Assert.AreEqual("b_mod", ModManager.Installed[0].Id, "installed mods are listed in load order");
        }

        [Test]
        public void MoveChangesTheLoadOrder()
        {
            WriteMod("a", "a_mod");
            WriteMod("b", "b_mod");
            WriteMod("c", "c_mod");
            ScanAndEnable("a_mod", "b_mod", "c_mod");
            ModManager.Move("c_mod", -1);
            CollectionAssert.AreEqual(new[] { "a_mod", "c_mod", "b_mod" }, ActiveIds());
            ModManager.Move("a_mod", -1);   // already first
            ModManager.Move("b_mod", +1);   // already last
            CollectionAssert.AreEqual(new[] { "a_mod", "c_mod", "b_mod" }, ActiveIds());
            Assert.AreEqual("a_mod\nc_mod\nb_mod", PlayerPrefs.GetString("mods_enabled"));
        }

        [Test]
        public void EveryChangeBumpsTheRevision()
        {
            WriteMod("a", "a_mod");
            int r = ModManager.Revision;
            ModManager.Scan();
            Assert.Greater(ModManager.Revision, r);
            r = ModManager.Revision;
            ModManager.SetEnabled("a_mod", true);
            Assert.Greater(ModManager.Revision, r);
            r = ModManager.Revision;
            ModManager.BeginSession(new string[0]);
            Assert.Greater(ModManager.Revision, r);
        }

        [Test]
        public void ABrokenManifestIsListedWithItsError()
        {
            string dir = WriteMod("broken", "broken_mod");
            System.IO.File.WriteAllText(System.IO.Path.Combine(dir, "mod.json"), "{ \"id\": \"broken_mod\",\n \"name\": }");
            ScanAndEnable("broken_mod");
            var m = ModManager.Installed.Single();
            Assert.IsTrue(m.Broken);
            StringAssert.StartsWith("mod.json line 2", m.Errors[0]);
            Assert.AreEqual(0, ModManager.UserMods.Count);
        }

        [Test]
        public void ABrokenContentFileMakesTheModUnusable()
        {
            WriteMod("bad", "bad_mod", "1.0", "", ("items.json", "{ 'not': 'a list' }"));
            ScanAndEnable("bad_mod");
            var m = Installed("bad_mod");
            Assert.IsTrue(m.Broken);
            StringAssert.Contains("items.json", m.Errors[0]);
            Assert.AreEqual(0, ModManager.UserMods.Count);
            Assert.AreEqual(m.Errors[0], ModManager.InactiveReason(m));
        }

        [Test]
        public void ADuplicateIdLoadsOnlyTheFirstFound()
        {
            WriteMod("a_first", "same_mod");
            WriteMod("b_second", "same_mod");
            ModManager.Scan();
            var both = ModManager.Installed.Where(m => m.Id == "same_mod").ToList();
            Assert.AreEqual(2, both.Count);
            Assert.AreEqual(1, both.Count(m => m.Broken));
            Assert.IsTrue(both.Single(m => m.Broken).Source.Location.EndsWith("b_second"));
            StringAssert.Contains("already uses the id", both.Single(m => m.Broken).Errors[0]);
            ModManager.SetEnabled("same_mod", true);
            Assert.AreEqual(1, ModManager.UserMods.Count);
            Assert.IsFalse(ModManager.UserMods[0].Broken);
        }

        // ---- dependencies ---------------------------------------------------------------------------------------------

        [Test]
        public void TurningOnAModTurnsOnWhatItNeedsFirst()
        {
            WriteMod("base", "base_mod");
            WriteMod("mid", "mid_mod", manifestExtra: ", 'dependencies': ['base_mod']");
            WriteMod("top", "top_mod", manifestExtra: ", 'dependencies': ['mid_mod']");
            ScanAndEnable("top_mod");
            CollectionAssert.AreEqual(new[] { "base_mod", "mid_mod", "top_mod" }, ActiveIds());
            Assert.AreEqual("base_mod\nmid_mod\ntop_mod", PlayerPrefs.GetString("mods_enabled"));
        }

        [Test]
        public void DependenciesComeFirstWhateverTheOrder()
        {
            WriteMod("base", "base_mod");
            WriteMod("child", "child_mod", manifestExtra: ", 'dependencies': ['base_mod']");
            PlayerPrefs.SetString("mods_enabled", "child_mod\nbase_mod");
            ModManager.Scan();
            CollectionAssert.AreEqual(new[] { "base_mod", "child_mod" }, ActiveIds());
        }

        [Test]
        public void TurningOffAModTurnsOffWhatNeedsIt()
        {
            WriteMod("base", "base_mod");
            WriteMod("mid", "mid_mod", manifestExtra: ", 'dependencies': ['base_mod']");
            WriteMod("top", "top_mod", manifestExtra: ", 'dependencies': ['mid_mod']");
            WriteMod("free", "free_mod");
            ScanAndEnable("top_mod", "free_mod");
            CollectionAssert.AreEquivalent(new[] { "mid_mod", "top_mod" }, ModManager.NeededBy("base_mod").Select(m => m.Id).ToArray());
            ModManager.SetEnabled("base_mod", false);
            CollectionAssert.AreEqual(new[] { "free_mod" }, ActiveIds());
            Assert.IsFalse(ModManager.IsEnabled("top_mod"));
        }

        [Test]
        public void AModWithAMissingDependencyIsntActive()
        {
            WriteMod("child", "child_mod", manifestExtra: ", 'dependencies': ['ghost_mod']");
            ScanAndEnable("child_mod");
            Assert.IsTrue(ModManager.IsEnabled("child_mod"));
            Assert.AreEqual(0, ModManager.UserMods.Count);
            StringAssert.Contains("isn't installed", ModManager.InactiveReason(Installed("child_mod")));
        }

        [Test]
        public void AModWithATooOldDependencyIsntActive()
        {
            WriteMod("parent", "parent_mod", "1.5");
            WriteMod("child", "child_mod", manifestExtra: ", 'dependencies': ['parent_mod>=2.0']");
            ScanAndEnable("child_mod");
            CollectionAssert.AreEqual(new[] { "parent_mod" }, ActiveIds());
            StringAssert.Contains("version 2.0 or later (installed: 1.5)", ModManager.InactiveReason(Installed("child_mod")));
            Assert.IsNull(ModManager.InactiveReason(Installed("parent_mod")));
        }

        [Test]
        public void ANewEnoughDependencyIsFine()
        {
            WriteMod("parent", "parent_mod", "2.10");
            WriteMod("child", "child_mod", manifestExtra: ", 'dependencies': ['parent_mod>=2.9']");
            ScanAndEnable("child_mod");
            CollectionAssert.AreEqual(new[] { "parent_mod", "child_mod" }, ActiveIds());
            Assert.IsNull(ModManager.InactiveReason(Installed("child_mod")));
        }

        [Test]
        public void ADependencyThatIsOffIsNamed()
        {
            WriteMod("base", "base_mod");
            WriteMod("child", "child_mod", manifestExtra: ", 'dependencies': ['base_mod']");
            PlayerPrefs.SetString("mods_enabled", "child_mod");
            ModManager.Scan();
            Assert.AreEqual(0, ModManager.UserMods.Count);
            StringAssert.Contains("on too", ModManager.InactiveReason(Installed("child_mod")));
        }

        [Test]
        public void ABrokenDependencyIsNamed()
        {
            WriteMod("base", "base_mod", "1.0", "", ("ships.json", "[ { 'id': 'x' } ]"));
            WriteMod("child", "child_mod", manifestExtra: ", 'dependencies': ['base_mod']");
            ScanAndEnable("child_mod");
            Assert.AreEqual(0, ModManager.UserMods.Count);
            StringAssert.Contains("has errors", ModManager.InactiveReason(Installed("child_mod")));
        }

        // ---- sessions -------------------------------------------------------------------------------------------------

        [Test]
        public void ASessionRunsExactlyTheHostsMods()
        {
            WriteMod("a", "a_mod");
            WriteMod("b", "b_mod");
            ScanAndEnable("a_mod");
            ModManager.BeginSession(new[] { "b_mod" });
            Assert.IsTrue(ModManager.InSession);
            CollectionAssert.AreEqual(new[] { "b_mod" }, ActiveIds());
            CollectionAssert.AreEqual(new[] { "a_mod" }, ModManager.SinglePlayerActive.Select(m => m.Id).ToArray());
            ModManager.BeginSession(null);
            Assert.AreEqual(0, ModManager.UserMods.Count, "a session without modded content has none");
            ModManager.EndSession();
            Assert.IsFalse(ModManager.InSession);
            CollectionAssert.AreEqual(new[] { "a_mod" }, ActiveIds());
        }

        [Test]
        public void ASessionNumbersItsModsAfreshWithoutTheRegistry()
        {
            WriteMod("a", "a_mod", "1.0", "", ("items.json", "[ { 'id': 'x', 'base': 2 } ]"));
            WriteMod("b", "b_mod", "1.0", "", ("items.json", "[ { 'id': 'y', 'base': 2 } ]"));
            ModManager.Scan();
            ModManager.BeginSession(new[] { "b_mod", "a_mod" });
            var db = GoF2Remake.Data.Database.Load();
            Assert.AreEqual(233, ModContent.ItemIndexOf("b_mod:y"), "in the session's order");
            Assert.AreEqual(234, ModContent.ItemIndexOf("a_mod:x"));
            Assert.AreEqual("b_mod:y", db.Item(233).modKey);
            Assert.AreEqual(0, ModRegistry.All(ModRegistry.Kind.Item).Count);
            Assert.IsFalse(System.IO.File.Exists(System.IO.Path.Combine(Sandbox, "mod_registry.json")));
        }

        [Test]
        public void ActiveSignatureListsTheActiveModsInOrder()
        {
            Assert.AreEqual("", ModManager.ActiveSignature);
            WriteMod("a", "a_mod", "1.2");
            WriteMod("b", "b_mod", "3");
            ScanAndEnable("b_mod", "a_mod");
            var b = Installed("b_mod");
            var a = Installed("a_mod");
            Assert.AreEqual($"b_mod@3#{b.Hash};a_mod@1.2#{a.Hash}", ModManager.ActiveSignature);
        }
    }
}
