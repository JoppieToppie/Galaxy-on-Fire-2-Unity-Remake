// ModProjectHistoryTests.cs
// Feature: the Mod Builder's undo / redo (ModProjectHistory, ModBuilder.History): an edit recorded at a save point is taken
// back and brought back again, in memory and in the project's files; a texture the builder overwrote or deleted comes back
// with its old content; nothing is recorded while the state is unchanged; a new edit drops the redo steps; the store goes
// with the history.

using System.IO;
using System.Linq;
using System.Text;
using GoF2Remake.Data;
using GoF2Remake.Modding;
using NUnit.Framework;

namespace GoF2Remake.Tests
{
    public class ModProjectHistoryTests : SavesModsTest
    {
        ModProjectHistory history;

        [TearDown]
        public void CloseHistory() { history?.Dispose(); history = null; }

        static string ItemName(ModProject p) => p.Items.Single().Name;

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
