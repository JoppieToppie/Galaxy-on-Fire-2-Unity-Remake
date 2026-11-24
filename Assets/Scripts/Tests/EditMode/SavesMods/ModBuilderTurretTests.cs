// ModBuilderTurretTests.cs
// Feature: the Mod Builder's turret items (ModBuilderView's Items kind, ProjectItem): what it writes into items.json reaches
// the game as the hand-written form would. A turret's own model with its size, muzzle and shot start (fx "turret") read by
// ModWeapons, the remake-only stats (missiles, salvos, the turret's range) on the item in the game's table, and a plain
// "turret": "file" from a hand edit kept when the builder adds a field.

using System.IO;
using System.Linq;
using GoF2Remake.Data;
using GoF2Remake.Modding;
using NUnit.Framework;

namespace GoF2Remake.Tests
{
    public class ModBuilderTurretTests : SavesModsTest
    {
        [TearDown]
        public void CloseProject() => ModManager.SetBuilderProject(null);

        /// <summary>A project with a new turret item based on 182 and a dummy model file to pick.</summary>
        (ModProject project, ProjectItem item, string glb) NewTurret()
        {
            var p = ModProject.Create("Turret test", "tests");
            var it = p.AddItem(182, "Hornet");
            string source = Path.Combine(Path.GetTempPath(), "gof2_turret_test.glb");
            File.WriteAllBytes(source, Dummy);
            return (p, it, source);
        }

        [Test]
        public void TheTurretModelReachesModWeapons()
        {
            var (p, it, source) = NewTurret();
            it.TurretModel = p.AddFile(source, "models", it.LocalId + "_turret", ".glb");
            it.TurretScale = 2.5f;
            it.TurretMuzzle = 28f;
            it.TurretShotStart = 32f;
            p.Save();
            ModManager.SetBuilderProject(p.Folder);
            var d = ModWeapons.Defs().Single();
            Assert.AreEqual(it.TurretModel, d.turretModel);
            Assert.AreEqual(2.5f, d.turretScale);
            Assert.AreEqual(28f, d.turretMuzzle);
            Assert.AreEqual(32f, d.turretShot);
            Assert.IsEmpty(ModManager.BuilderProject.Warnings, AllMessages(ModManager.BuilderProject));
        }

        /// <summary>The project's items.json as written, without spaces or line breaks.</summary>
        static string ItemsJson(ModProject p)
        {
            p.Save();
            return new string(File.ReadAllText(Path.Combine(p.Folder, ModContent.ItemsFile)).Where(c => !char.IsWhiteSpace(c)).ToArray());
        }

        [Test]
        public void ClearingTheModelKeepsTheRestAndDropsAnEmptyTurret()
        {
            var (p, it, source) = NewTurret();
            it.TurretModel = p.AddFile(source, "models", "t", ".glb");
            it.TurretScale = 2f;
            StringAssert.Contains("\"turret\":{", ItemsJson(p));
            it.TurretModel = null;
            StringAssert.Contains("\"turret\":{\"scale\":2", ItemsJson(p), "the base item's model, resized");
            it.TurretScale = null;
            StringAssert.DoesNotContain("\"fx\"", ItemsJson(p), "no empty fx left behind");
        }

        [Test]
        public void TheBaseTurretResizedAndMovedReachesModWeapons()
        {
            var (p, it, _) = NewTurret();
            it.TurretScale = 1.5f;
            it.TurretOffset = new UnityEngine.Vector3Int(0, 40, -10);
            p.Save();
            ModManager.SetBuilderProject(p.Folder);
            var d = ModWeapons.Defs().Single();
            Assert.IsNull(d.turretModel);
            Assert.AreEqual(1.5f, d.turretScale);
            Assert.AreEqual(new UnityEngine.Vector3(0f, 40f, -10f), d.turretOffset);
        }

        [Test]
        public void ASideTurretMountIsWrittenAsTurretUp()
        {
            var p = ModProject.Create("Side turret", "tests");
            var s = p.AddOverride(Database.Shared, 28);
            s.Mounts.Clear();
            var side = new WeaponMount { slotType = 2, position_engine = new[] { 100, 0, 0 }, turretScale = 2.5f };
            side.SetTurretUp(new UnityEngine.Vector3(1f, 0f, 0f));
            var under = new WeaponMount { slotType = 2, position_engine = new[] { 0, -50, 0 } };
            under.SetTurretUp(new UnityEngine.Vector3(0f, -1f, 0f));
            var top = new WeaponMount { slotType = 2, position_engine = new[] { 0, 50, 0 } };
            top.SetTurretUp(new UnityEngine.Vector3(0f, 1f, 0f));
            s.Mounts.AddRange(new[] { side, under, top });
            p.Save();
            string json = new string(File.ReadAllText(Path.Combine(p.Folder, ModContent.ShipsFile)).Where(c => !char.IsWhiteSpace(c)).ToArray());
            StringAssert.Contains("\"turretUp\":[1.0,0.0,0.0]", json);
            StringAssert.Contains("\"turretScale\":2.5", json, "the per-mount scale is exported");
            StringAssert.Contains("\"position_engine\":[0,-50,0],\"upsideDown\":true", json, "straight down as before");
            StringAssert.DoesNotContain("\"position_engine\":[0,50,0],\"turretUp\"", json, "straight up needs nothing");
            var back = ModProject.Load(p.Folder).Ships.Single().Mounts;
            CollectionAssert.AreEqual(new[] { 1f, 0f, 0f }, back[0].turretUp);
            Assert.AreEqual(2.5f, back[0].TurretScale, "the scale survives a project save and reload");
            Assert.IsTrue(back[1].TurretHangs);
        }

        [Test]
        public void APlainFileFromAHandEditBecomesTheObject()
        {
            var p = ModProject.Create("Hand edit", "tests");
            File.WriteAllText(Path.Combine(p.Folder, ModContent.ItemsFile),
                Q("[ { 'id': 'x', 'base': 182, 'fx': { 'turret': 'models/a.glb', 'shot': 46 } } ]"));
            p = ModProject.Load(p.Folder);
            var it = p.Items.Single();
            Assert.AreEqual("models/a.glb", it.TurretModel);
            Assert.IsNull(it.TurretMuzzle);
            it.TurretMuzzle = 50f;
            string json = ItemsJson(p);
            StringAssert.Contains("\"turret\":{\"model\":\"models/a.glb\",\"muzzle\":50", json);
            StringAssert.Contains("\"shot\":46", json, "the rest of fx stays");
        }

        [Test]
        public void TheRemakeStatsReachTheItem()
        {
            var (p, it, _) = NewTurret();
            it.SetStat(105, "missiles", 1);
            it.SetStat(106, "salvoShots", 2);
            it.SetStat(109, "turretRange", 30000);
            p.Save();
            ModManager.SetBuilderProject(p.Folder);
            var db = Database.Load();
            var item = db.Item(ModContent.ItemIndexOf(p.Id + ":" + it.LocalId));
            Assert.IsNotNull(item, AllMessages(ModManager.BuilderProject));
            Assert.AreEqual(1, item.Attr(105));
            Assert.AreEqual(2, item.Attr(106));
            Assert.AreEqual(30000, item.Attr(109));
        }
    }
}
