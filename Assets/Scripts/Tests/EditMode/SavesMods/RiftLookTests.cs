// RiftLookTests.cs
// Feature: docs/notes/navigation.md "Rifts" (the looks): a mod rift entry's "look" (Modding.ModEncounters.RiftLook): its
// systems, rift and portal models, materials and sky; the run in a system takes the look naming it, else one for anywhere;
// its models are loaded with the orbits' (ModWorld.ContainerModels); the portal opens, holds and closes (World.RiftPortal).

using System.Linq;
using GoF2Remake.Data;
using GoF2Remake.Modding;
using NUnit.Framework;
using UnityEngine;

namespace GoF2Remake.Tests
{
    public class RiftLookTests : SavesModsTest
    {
        [Test]
        public void ALookIsReadAndPickedBySystem()
        {
            WriteMod("w", "t_rift", "1.0", "", ("rift/r.glb", "x"), ("rift/p.glb", "x"), ("sky/s.png", "x"),
                ("systems.json", "[ { 'id': 'far', 'name': 'Far', 'race': 1, 'position': [1, 2, 3] } ]"),
                ("stations.json", "[ { 'id': 'post', 'system': 't_rift:far', 'name': 'Post' } ]"),
                ("encounters.json", "[ { 'id': 'r1', 'kind': 'rift', 'bosses': [ { 'ship': 'x:y' } ], 'look': { 'systems': ['t_rift:far'], " +
                    "'rift': { 'model': 'rift/r.glb', 'size': 40000 }, 'portal': { 'model': 'rift/p.glb' }, " +
                    "'materials': [ { 'material': 'veins', 'additive': true } ], 'skybox': { 'nebula': 'sky/s.png', 'stars': 'none' } } }, " +
                    "{ 'id': 'r2', 'kind': 'rift', 'look': { 'portal': { 'model': 'rift/p.glb', 'size': 5000 } } } ]"));
            ScanAndEnable("t_rift");
            Database.Load();
            Assert.IsTrue(ModWorld.TryResolveSystem("t_rift:far", out int far));
            var looks = ModEncounters.RiftLooks();
            Assert.AreEqual(2, looks.Count);
            var mine = ModEncounters.RiftLookFor(far);
            Assert.AreEqual("rift/r.glb", mine.rift);
            Assert.AreEqual(40000f, mine.riftSize);
            Assert.AreEqual(8000f, mine.portalSize, "the default");
            Assert.AreEqual(1, mine.materials.Count);
            Assert.AreEqual("mod:t_rift|sky/s.png", mine.skybox.nebula);
            Assert.AreEqual(-2, mine.skybox.starsLayer);
            var elsewhere = ModEncounters.RiftLookFor(0);
            Assert.IsNull(elsewhere.rift, "another system: the look for anywhere");
            Assert.AreEqual(5000f, elsewhere.portalSize);
            var models = ModWorld.ContainerModels().Where(m => m.mod.Id == "t_rift").Select(m => m.path).ToList();
            CollectionAssert.Contains(models, "rift/r.glb");
            CollectionAssert.Contains(models, "rift/p.glb");
        }

        [Test]
        public void AMissingLookModelIsAWarning()
        {
            WriteMod("w", "t_rift", "1.0", "", ("encounters.json", "[ { 'id': 'r1', 'kind': 'rift', 'look': { 'rift': { 'model': 'none.glb' } } } ]"));
            ScanAndEnable("t_rift");
            Database.Load();
            Assert.AreEqual(0, ModEncounters.RiftLooks().Count);
            StringAssert.Contains("isn't in the mod", string.Join(" | ", Installed("t_rift").Warnings));
        }

        [Test]
        public void ThePortalOpensHoldsAndCloses()
        {
            Assert.AreEqual(0f, GoF2Remake.World.RiftPortal.Opening(0f));
            Assert.AreEqual(1f, GoF2Remake.World.RiftPortal.Opening(GoF2Remake.World.RiftPortal.OpenS + 0.5f));
            Assert.Less(GoF2Remake.World.RiftPortal.Opening(GoF2Remake.World.RiftPortal.OpenS + GoF2Remake.World.RiftPortal.HoldS + 0.4f), 1f);
            Assert.Less(GoF2Remake.World.RiftPortal.Opening(5f), 0f, "closed: gone");
        }
    }
}
