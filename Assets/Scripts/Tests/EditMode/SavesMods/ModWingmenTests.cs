// ModWingmenTests.cs
// Feature: Modding/README.md "Wingmen" (Modding.ModWingmen, Data.WingmanCrew): a mod's wingmen.json makes a crew of its
// ships with a "wingmanDevice" (or the ones it names), with its chance and radio lines; a wrong device or line kind is
// warned about.

using System.Collections.Generic;
using GoF2Remake.Data;
using GoF2Remake.Modding;
using NUnit.Framework;

namespace GoF2Remake.Tests
{
    public class ModWingmenTests : SavesModsTest
    {
        Database Load(string wingmen)
        {
            string dir = WriteMod("wm", "t_wing", "1.0", "",
                ("ships.json", "[ { 'id': 'medic', 'model': 'y.glb', 'name': 'Medic', 'race': 0, 'wingmanDevice': 'repair' }, " +
                               "{ 'id': 'odd', 'model': 'y.glb', 'name': 'Odd', 'race': 2, 'wingmanDevice': 'nope' }, " +
                               "{ 'id': 'plain', 'model': 'y.glb', 'name': 'Plain', 'race': 2 } ]"),
                ("wingmen.json", wingmen));
            WriteFile(dir, "y.glb", Dummy);
            ScanAndEnable("t_wing");
            return Database.Load();
        }

        [Test]
        public void ACrewFliesTheModsWingmanShips()
        {
            var db = Load("{ 'chance': 100, 'radio': { 'kill': ['Got one!', 'Scratched one!'], 'thanks': 'Nice shot!', 'nope': ['x'] } }");
            Assert.AreEqual(1, db.WingmanCrews.Count);
            var crew = db.WingmanCrews[0];
            Assert.IsTrue(ModContent.TryResolveShip("t_wing:medic", out int medic));
            CollectionAssert.AreEqual(new[] { medic }, crew.ships, "only the ship with a device it knows");
            Assert.AreEqual(100, crew.chance);
            Assert.AreSame(crew, ModWingmen.Roll(db, n => n - 1));
            Assert.AreSame(crew, ModWingmen.Of(db, medic));
            Assert.IsTrue(ModContent.TryResolveShip("t_wing:plain", out int plain));
            Assert.IsNull(ModWingmen.Of(db, plain));
            Assert.AreEqual("Scratched one!", crew.Line(WingmanCrew.Kill, n => n - 1));
            Assert.AreEqual("Nice shot!", crew.Line(WingmanCrew.Thanks, n => 0), "one line alone");
            Assert.IsNull(crew.Line(WingmanCrew.Help, n => 0), "none of that kind");
            CollectionAssert.AreEqual(new[] { medic, medic }, crew.Pick(2, 2, n => 0), "no Nivelian ship: any of them");
            string all = AllMessages(Installed("t_wing"));
            StringAssert.Contains("isn't repair, ammo or charged", all);
            StringAssert.Contains("no line kind \"nope\"", all);
            Assert.AreEqual("repair", CustomShips.Get(medic).wingmanDevice);
        }

        [Test]
        public void ItsShipsCanBeNamedAndItsChanceTakesTurns()
        {
            var db = Load("{ 'chance': 30, 'ships': ['plain', 'medic', 'nope:x'] }");
            var crew = db.WingmanCrews[0];
            Assert.IsTrue(ModContent.TryResolveShip("t_wing:plain", out int plain));
            Assert.IsTrue(ModContent.TryResolveShip("t_wing:medic", out int medic));
            CollectionAssert.AreEqual(new[] { plain, medic }, crew.ships);
            CollectionAssert.AreEqual(new[] { plain }, crew.Pick(2, 1, n => 0), "a Nivelian agent: its race's ship");
            Assert.IsNull(ModWingmen.Roll(db, n => 30), "30 % misses at 30");
            Assert.AreSame(crew, ModWingmen.Roll(db, n => 29));
            StringAssert.Contains("is no ship", AllMessages(Installed("t_wing")));
        }
    }
}
