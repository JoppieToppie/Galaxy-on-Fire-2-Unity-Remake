// ModCratesTests.cs
// Feature: docs/notes/mods.md "Crates" (Modding.ModCrates): ships.json "crate" (a glTF / GLB file in the mod) is the loot
// container a ship of that type leaves, keyed "<mod id>|<path>" (ships sharing a file share the model, the first
// "crateSize" wins); a ship without one, or a crate made before the model is loaded, keeps its race's container.

using System.Linq;
using GoF2Remake.Data;
using GoF2Remake.Flight;
using GoF2Remake.Modding;
using NUnit.Framework;
using UnityEngine;

namespace GoF2Remake.Tests
{
    public class ModCratesTests : SavesModsTest
    {
        [Test]
        public void AShipsOwnCrateIsKeyedByItsModAndFile()
        {
            string dir = WriteMod("c", "t_crate", "1.0", "", ("ships.json",
                "[ { 'id': 'a', 'name': 'A', 'race': 0, 'model': 'a.glb', 'crate': 'box.glb', 'crateSize': 400 }," +
                "  { 'id': 'b', 'name': 'B', 'race': 3, 'model': 'a.glb', 'crate': 'box.glb', 'crateSize': 900 }," +
                "  { 'id': 'c', 'name': 'C', 'race': 1, 'model': 'a.glb' } ]"));
            WriteFile(dir, "a.glb", Dummy);
            WriteFile(dir, "box.glb", Dummy);
            ScanAndEnable("t_crate");
            Database.Load();
            int a = ModContent.ShipIndexOf("t_crate:a"), b = ModContent.ShipIndexOf("t_crate:b"), c = ModContent.ShipIndexOf("t_crate:c");
            Assert.AreEqual("box.glb", CustomShips.Get(a).crate);
            Assert.AreEqual(400f, CustomShips.Get(a).crateSize);
            Assert.AreEqual("t_crate|box.glb", ModCrates.KeyOf(a));
            Assert.AreEqual("t_crate|box.glb", ModCrates.KeyOf(b), "the same file: the same model");
            Assert.IsNull(ModCrates.KeyOf(c), "no \"crate\": the race's container");
            Assert.IsNull(ModCrates.KeyOf(14), "a game ship");
            Assert.IsFalse(ModManager.Find("t_crate").Warnings.Any(w => w.Contains("crate")), AllMessages(ModManager.Find("t_crate")));
        }

        [Test]
        public void ACrateWhoseModelIsntLoadedIsTheRacesContainer()
        {
            var crate = Crate.Spawn(Vector3.zero, new[] { new ItemStack(1, 2) }, 0, Crate.LookContainer, "t_none|missing.glb");
            try
            {
                Assert.AreEqual("t_none|missing.glb", crate.model, "the key stays for the other players' copies (NetCrate)");
                Assert.IsNull(ModCrates.Instantiate("t_none|missing.glb", 0, Vector3.zero, Quaternion.identity));
                Assert.IsNull(ModCrates.Instantiate(null, 0, Vector3.zero, Quaternion.identity));
                var assets = CombatAssets.Load();
                if (assets != null && assets.Crate(0) != null)
                    Assert.IsNotNull(crate.GetComponentInChildren<MeshFilter>(), "the Terran container stands in");
            }
            finally { Object.DestroyImmediate(crate.gameObject); }
        }
    }
}
