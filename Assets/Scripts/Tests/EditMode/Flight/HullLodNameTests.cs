// HullLodNameTests.cs
// Feature: docs/notes/npcs-and-combat.md "Mesh collision": the hull is LOD 0's meshes. A mesh named as a lower level
// ("_lod_" and a number above 0, the game's "battleship_terran_lod_1", or AutoLods' "_lod_gen1") is left out; LOD 0 by
// name is a hull (Galaxy on Fire 3's "..._lod_000_body": the GoF3 Systems mod's stations, gates and traffic had no hull,
// and the player met the box round the whole model).

using GoF2Remake.Flight;
using NUnit.Framework;

namespace GoF2Remake.Tests
{
    public class HullLodNameTests
    {
        [TestCase("battleship_terran_lod_1", true)]
        [TestCase("asteroid_01_lod_3", true)]
        [TestCase("structure_mining_station_scaffold_002_lod_001_body", true)]
        [TestCase("hull_lod_gen1", true)]
        [TestCase("structure_mining_station_scaffold_002_lod_000_body", false)]
        [TestCase("structure_vossk_jumpgate_000_lod_000_ring_01", false)]
        [TestCase("structure_terran_largeship_000", false)]
        [TestCase("station_terran_01", false)]
        [TestCase("ship_lod_", false)]
        public void LowerLevelsByName(string name, bool lower)
        {
            Assert.AreEqual(lower, HullCollision.LowerLodName(name));
        }
    }
}
