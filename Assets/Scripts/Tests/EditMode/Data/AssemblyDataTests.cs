// AssemblyDataTests.cs
// CLAUDE.md "Assembled prefabs": assemblies.json (build_assemblies.py) lists one assembled object per game object, built by
// "GoF2 > Build > Assembled Prefabs" into Resources/Assembled/<pack>/<category>/<name>.prefab (AssembledObject.PrefabPath),
// loaded by name at run time (AssembledObject.LoadPrefab). Every entry must have its prefab, names are unique (Database
// finds them by name), the packs are the original's three (main, valkyrie = v_, supernova = sn_), and the rooms the station
// scene builds (StationTables.HangarRoom, the hangars and bars) are among them.

using System.Collections.Generic;
using System.Linq;
using GoF2Remake.Data;
using GoF2Remake.World;
using NUnit.Framework;

namespace GoF2Remake.Tests
{
    [Unity.Scripting.LifecycleManagement.NoAutoStaticsCleanup]
    public class AssemblyDataTests : SandboxTest
    {
        static List<AssemblyData> Originals() => Database.Load().Assemblies.Where(a => a.pack != "mod").ToList();

        [Test]
        public void EveryAssemblyHasItsPrefab()
        {
            var list = Originals();
            Assert.Greater(list.Count, 300, "assemblies.json");
            var missing = list.Where(a => !DataUiTestKit.AssembledExists(a)).Select(a => a.pack + "/" + a.category + "/" + a.name).ToList();
            Assert.IsEmpty(missing, "assemblies without a prefab (run GoF2 > Build > Assembled Prefabs)");
        }

        [Test]
        public void AssemblyNamesAreUnique()
        {
            var dup = Originals().GroupBy(a => a.name).Where(g => g.Count() > 1).Select(g => g.Key).ToList();
            Assert.IsEmpty(dup);
        }

        [Test]
        public void AssembliesBelongToTheOriginalPacks()
        {
            foreach (var a in Originals())
            {
                CollectionAssert.Contains(new[] { "main", "valkyrie", "supernova" }, a.pack, a.name);
                Assert.IsFalse(string.IsNullOrEmpty(a.category), a.name);
                if (a.category == "ships" && a.name.StartsWith("v_ship_")) Assert.AreEqual("valkyrie", a.pack, a.name);
                if (a.category == "ships" && a.name.StartsWith("sn_ship_")) Assert.AreEqual("supernova", a.pack, a.name);
            }
        }

        [Test]
        public void TheStationScenesRoomsAreAssembled()
        {
            var db = Database.Load();
            foreach (var room in StationTables.HangarRoom)
            {
                if (room == null) continue;
                var a = db.Assemblies.FirstOrDefault(x => x.name == room || x.name == "v_" + room);
                Assert.IsNotNull(a, "hangar " + room);
                Assert.IsTrue(DataUiTestKit.AssembledExists(a), room);
            }
            foreach (var bar in new[] { "bar_terran", "bar_vossk", "bar_nivelian", "bar_midorian" })
                Assert.IsNotNull(db.AssemblyByName(bar), bar);
        }

        [Test]
        public void TheSpecialShipsAreAssembled()
        {
            var db = Database.Load();
            // Globals::getShipGroup's freighters and battleship, the capital ships of the Debug Ships tab and the stations'
            // Void / battlestation models (CLAUDE.md "Space scene", Alien orbit).
            foreach (var name in new[] { "cargo_001_midorian", "cargo_002_nivelian", "cargo_003_terran", "cargo_004_vossk", "battleship_terran",
                                         "station_void", "station_pirates" })
            {
                var a = db.AssemblyByName(name);
                Assert.IsNotNull(a, name);
                Assert.IsTrue(DataUiTestKit.AssembledExists(a), name);
            }
        }
    }
}
