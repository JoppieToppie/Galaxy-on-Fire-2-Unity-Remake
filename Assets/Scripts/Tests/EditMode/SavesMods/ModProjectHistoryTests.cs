// ModProjectHistoryTests.cs
// Feature: the Mod Builder's undo / redo (ModProjectHistory, ModBuilder.History): an edit recorded at a save point is taken
// back and brought back again, in memory and in the project's files; a texture the builder overwrote or deleted comes back
// with its old content; nothing is recorded while the state is unchanged; a new edit drops the redo steps; the store goes
// with the history.

using System;
using System.IO;
using System.Linq;
using System.Text;
using GoF2Remake.Data;
using GoF2Remake.Modding;
using NUnit.Framework;
using Newtonsoft.Json.Linq;

namespace GoF2Remake.Tests
{
    public class ModProjectHistoryTests : SavesModsTest
    {
        ModProjectHistory history;

        [TearDown]
        public void CloseHistory() { history?.Dispose(); history = null; }

        static string ItemName(ModProject p) => p.Items.Single().Name;

        [TestCase(ModFlight.File, "flightRules")]
        [TestCase(ModPlayerInfrastructure.FileName, "playerInfrastructure")]
        [TestCase(ModNpcFactionWar.File, "npcFactionWar")]
        [TestCase(ModAdvancedEconomy.File, "advancedEconomy")]
        public void ExternalConfigurationIsLoadedAndNotRewritten(string file, string key)
        {
            var p = ModProject.Create("Config retention", "tests");
            string path = Path.Combine(p.Folder, file);
            const string external = "{ \"enabled\": false, \"external\": 17 }";
            File.WriteAllText(path, external);
            p.Save();
            Assert.AreEqual(17, (int)p.CaptureState()[key]["external"]);
            Assert.AreEqual(external, File.ReadAllText(path));
            File.WriteAllText(path, "{\"external\": 25}");
            p.Save();
            Assert.AreEqual(25, (int)p.CaptureState()[key]["external"]);
            Assert.AreEqual("{\"external\": 25}", File.ReadAllText(path));
        }

        [Test]
        public void ConflictingEconomyEditsAreRetainedOnBothSides()
        {
            var p = ModProject.Create("Conflict", "tests");
            p.AdvancedEconomyRules["tickIntervalSeconds"] = 900;
            p.Save();
            string path = Path.Combine(p.Folder, ModAdvancedEconomy.File);
            p.AdvancedEconomyRules["tickIntervalSeconds"] = 600;
            File.WriteAllText(path, "{\"tickIntervalSeconds\":1200}");
            var error = Assert.Throws<IOException>(() => p.Save());
            StringAssert.Contains(ModAdvancedEconomy.File, error.Message);
            Assert.AreEqual(600, (int)p.AdvancedEconomyRules["tickIntervalSeconds"]);
            Assert.AreEqual(1200, (int)JObject.Parse(File.ReadAllText(path))["tickIntervalSeconds"]);
        }

        [Test]
        public void EmptyConfigurationRequiresExplicitDeletionAndUndoRestoresIt()
        {
            var p = ModProject.Create("Empty economy", "tests");
            p.AdvancedEconomyRules["enabled"] = false;
            p.Save();
            string path = Path.Combine(p.Folder, ModAdvancedEconomy.File);
            p.AdvancedEconomyRules.RemoveAll();
            p.Save();
            Assert.IsTrue(File.Exists(path));
            Assert.IsEmpty(JObject.Parse(File.ReadAllText(path)));
            history = new ModProjectHistory(p);
            var state = p.CaptureState();
            state["advancedEconomy"] = JValue.CreateNull();
            p.RestoreState(state);
            p.Save();
            history.Commit();
            Assert.IsFalse(File.Exists(path));
            Assert.IsTrue(history.Undo());
            Assert.IsTrue(File.Exists(path));
            Assert.IsEmpty(JObject.Parse(File.ReadAllText(path)));
            Assert.IsTrue(history.Redo());
            Assert.IsFalse(File.Exists(path));
        }

        [Test]
        public void StagedFactionWarIsNotSilentlyRemovedOrDuplicated()
        {
            var p = ModProject.Create("Staged warfare", "tests");
            string staged = Path.Combine(p.Folder, ModNpcFactionWar.StagedFile);
            Directory.CreateDirectory(Path.GetDirectoryName(staged));
            File.WriteAllText(staged, "{\"enabled\":false}");
            p.Save();
            Assert.IsTrue(File.Exists(staged));
            Assert.IsFalse(File.Exists(Path.Combine(p.Folder, ModNpcFactionWar.File)));
            p.NpcFactionWarRules["enabled"] = true;
            p.Save();
            Assert.IsTrue((bool)JObject.Parse(File.ReadAllText(staged))["enabled"]);
            history = new ModProjectHistory(p);
            var state = p.CaptureState();
            state["npcFactionWar"] = JValue.CreateNull();
            p.RestoreState(state);
            p.Save();
            history.Commit();
            Assert.IsFalse(File.Exists(staged));
            Assert.IsTrue(history.Undo());
            var restored = ModProject.Load(p.Folder);
            Assert.IsTrue((bool)restored.NpcFactionWarRules["enabled"]);
        }

        [Test]
        public void InvalidExternalJsonDoesNotReplaceTheLastLoadedConfiguration()
        {
            var p = ModProject.Create("Invalid external JSON", "tests");
            p.AdvancedEconomyRules["enabled"] = false;
            p.Save();
            string path = Path.Combine(p.Folder, ModAdvancedEconomy.File);
            File.WriteAllText(path, "{broken");
            Assert.Throws<IOException>(() => p.Save());
            Assert.IsFalse((bool)p.AdvancedEconomyRules["enabled"]);
            Assert.AreEqual("{broken", File.ReadAllText(path));
        }

        [Test]
        public void UnchangedFilesKeepTheirTimestamp()
        {
            var p = ModProject.Create("Unchanged", "tests");
            string path = Path.Combine(p.Folder, ModManifest.FileName);
            File.SetLastWriteTimeUtc(path, new DateTime(2020, 1, 1, 0, 0, 0, DateTimeKind.Utc));
            var original = File.GetLastWriteTimeUtc(path);
            p.Save();
            Assert.AreEqual(original, File.GetLastWriteTimeUtc(path));
        }

        [Test]
        public void LockedChangedFilePreventsPartialPublicationAndCanBeRetried()
        {
            Assume.That(Environment.OSVersion.Platform, Is.EqualTo(PlatformID.Win32NT), "Windows sharing semantics");
            var p = ModProject.Create("Locked configuration", "tests");
            p.AddItem(182, "Before");
            p.AdvancedEconomyRules["enabled"] = false;
            p.Save();
            string economy = Path.Combine(p.Folder, ModAdvancedEconomy.File);
            string items = Path.Combine(p.Folder, ModContent.ItemsFile);
            string before = File.ReadAllText(items);
            p.Items.Single().Name = "After";
            p.AdvancedEconomyRules["enabled"] = true;
            using (var locked = new FileStream(economy, FileMode.Open, FileAccess.Read, FileShare.Read))
            {
                var error = Assert.Throws<IOException>(() => p.Save());
                StringAssert.Contains(ModAdvancedEconomy.File, error.Message);
                Assert.AreEqual(before, File.ReadAllText(items));
            }
            Assert.IsFalse((bool)JObject.Parse(File.ReadAllText(economy))["enabled"]);
            p.Save();
            Assert.IsTrue((bool)JObject.Parse(File.ReadAllText(economy))["enabled"]);
            StringAssert.Contains("After", File.ReadAllText(items));
        }

        [Test]
        public void InterruptedSaveJournalRestoresPreviousFileOnLoad()
        {
            var p = ModProject.Create("Interrupted", "tests");
            p.AdvancedEconomyRules["enabled"] = false;
            p.Save();
            string path = Path.Combine(p.Folder, ModAdvancedEconomy.File);
            byte[] original = File.ReadAllBytes(path);
            byte[] intended = Encoding.UTF8.GetBytes("{\"enabled\":true}");
            string journal = p.Folder + ".save-recovery.json";
            File.WriteAllText(journal, new JArray(new JObject
            {
                ["path"] = ModAdvancedEconomy.File,
                ["before"] = Convert.ToBase64String(original),
                ["after"] = Convert.ToBase64String(intended)
            }).ToString());
            File.WriteAllBytes(path, intended);
            var loaded = ModProject.Load(p.Folder);
            Assert.IsNotNull(loaded);
            Assert.IsFalse((bool)loaded.AdvancedEconomyRules["enabled"]);
            CollectionAssert.AreEqual(original, File.ReadAllBytes(path));
            Assert.IsFalse(File.Exists(journal));
        }

        [Test]
        public void InterruptedSaveRecoveryRefusesToOverwriteThirdPartyData()
        {
            var p = ModProject.Create("Interrupted conflict", "tests");
            p.AdvancedEconomyRules["enabled"] = false;
            p.Save();
            string path = Path.Combine(p.Folder, ModAdvancedEconomy.File);
            string journal = p.Folder + ".save-recovery.json";
            File.WriteAllText(journal, new JArray(new JObject
            {
                ["path"] = ModAdvancedEconomy.File,
                ["before"] = Convert.ToBase64String(File.ReadAllBytes(path)),
                ["after"] = Convert.ToBase64String(Encoding.UTF8.GetBytes("{\"enabled\":true}"))
            }).ToString());
            const string external = "{\"enabled\":false,\"external\":true}";
            File.WriteAllText(path, external);
            Assert.Throws<IOException>(() => p.Save());
            Assert.AreEqual(external, File.ReadAllText(path));
            Assert.IsTrue(File.Exists(journal));
        }

        [Test]
        public void FactionWarRulesSurviveSaveLoadAndUndo()
        {
            var p = ModProject.Create("War history", "tests");
            p.Save();
            history = new ModProjectHistory(p);
            p.NpcFactionWarRules["frontlineSystems"] = new Newtonsoft.Json.Linq.JArray(3, 13);
            p.NpcFactionWarRules["dynamicFrontlines"] = false;
            p.Save();
            Assert.IsTrue(history.Commit());
            var loaded = ModProject.Load(p.Folder);
            Assert.IsTrue(Newtonsoft.Json.Linq.JToken.DeepEquals(p.NpcFactionWarRules, loaded.NpcFactionWarRules));
            Assert.IsTrue(history.Undo());
            Assert.IsEmpty(p.NpcFactionWarRules);
            Assert.IsFalse(File.Exists(Path.Combine(p.Folder, ModNpcFactionWar.File)));
            Assert.IsTrue(history.Redo());
            Assert.AreEqual(2, ((Newtonsoft.Json.Linq.JArray)p.NpcFactionWarRules["frontlineSystems"]).Count);
        }

        [Test]
        public void AnEditIsUndoneAndRedone()
        {
            var p = ModProject.Create("History test", "tests");
            var it = p.AddItem(182, "Hornet");
            p.Save();
            history = new ModProjectHistory(p);
            it.Name = "Wasp";
            p.Save();
            Assert.IsTrue(history.Commit());
            Assert.IsTrue(history.CanUndo);

            Assert.IsTrue(history.Undo());
            Assert.AreEqual("Hornet", ItemName(p));
            StringAssert.Contains("Hornet", File.ReadAllText(Path.Combine(p.Folder, ModContent.ItemsFile)), "saved as well");
            Assert.IsTrue(history.CanRedo);

            Assert.IsTrue(history.Redo());
            Assert.AreEqual("Wasp", ItemName(p));
            Assert.IsFalse(history.CanRedo);
        }

        [Test]
        public void AnEditNotCommittedYetIsUndoneToo()
        {
            var p = ModProject.Create("History test", "tests");
            p.AddItem(182, "Hornet");
            history = new ModProjectHistory(p);
            p.AddItem(46, "Second");
            Assert.IsTrue(history.Undo());
            Assert.AreEqual(1, p.Items.Count);
            Assert.IsTrue(history.Redo());
            Assert.AreEqual(2, p.Items.Count);
        }

        [Test]
        public void NothingIsRecordedWhileUnchangedAndANewEditDropsRedo()
        {
            var p = ModProject.Create("History test", "tests");
            var it = p.AddItem(182, "Hornet");
            history = new ModProjectHistory(p);
            Assert.IsFalse(history.Commit());
            Assert.IsFalse(history.Undo());

            it.Name = "Wasp";
            history.Commit();
            history.Undo();
            Assert.IsTrue(history.CanRedo);
            p.Items.Single().Name = "Bee";   // the restored entries are new objects
            history.Commit();
            Assert.IsFalse(history.CanRedo);
            Assert.AreEqual(1, history.UndoCount);
        }

        [Test]
        public void AnOverwrittenOrRemovedTextureComesBack()
        {
            var p = ModProject.Create("History test", "tests");
            history = new ModProjectHistory(p);
            string a = Path.Combine(TempFolder(), "a.png"), b = Path.Combine(TempFolder(), "b.png");
            File.WriteAllBytes(a, Encoding.ASCII.GetBytes("first"));
            File.WriteAllBytes(b, Encoding.ASCII.GetBytes("second, longer"));
            string tex = Path.Combine(p.Folder, ModContent.TexturesFolder, "ship_028_terran_diffuse.png");

            p.SetSkinTexture("ship_028_terran_diffuse", a);
            history.Commit();
            p.SetSkinTexture("ship_028_terran_diffuse", b);
            history.Commit();
            p.RemoveSkinTexture("ship_028_terran_diffuse");
            history.Commit();
            Assert.IsFalse(File.Exists(tex));

            history.Undo();
            Assert.AreEqual("second, longer", File.ReadAllText(tex));
            history.Undo();
            Assert.AreEqual("first", File.ReadAllText(tex));
            history.Undo();
            Assert.IsFalse(File.Exists(tex), "before the first skin texture");
            Assert.IsEmpty(p.SkinShips());
            history.Redo();
            Assert.AreEqual("first", File.ReadAllText(tex));
            Assert.AreEqual(new[] { 28 }, p.SkinShips().ToArray());
        }

        [Test]
        public void AbsorbJoinsTheLastStep()
        {
            var p = ModProject.Create("History test", "tests");
            var it = p.AddItem(182, "Hornet");
            history = new ModProjectHistory(p);
            it.Name = "Wasp";
            history.Commit();
            p.Items.Single().TechLevel = 5;   // made by the builder itself
            history.Absorb();
            Assert.AreEqual(1, history.UndoCount);
            history.Undo();
            Assert.AreEqual("Hornet", ItemName(p));
            history.Redo();
            Assert.AreEqual(5, p.Items.Single().TechLevel, "the absorbed change is part of the step");
        }

        [Test]
        public void TheStoreGoesWithTheHistory()
        {
            var p = ModProject.Create("History test", "tests");
            history = new ModProjectHistory(p);
            Assert.AreEqual(1, Directory.GetDirectories(ModProjectHistory.StoreRoot).Length);
            history.Dispose();
            history = null;
            Assert.AreEqual(0, Directory.GetDirectories(ModProjectHistory.StoreRoot).Length);
        }

        static string TempFolder()
        {
            string dir = Path.Combine(GamePaths.Data, "history_sources");
            Directory.CreateDirectory(dir);
            return dir;
        }
    }
}
