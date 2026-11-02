// ModderRequestsFlightTests.cs
// A modder's requests of 2026-11-02 in the real Space scene, with a test mod: an npcships.json capital ship like the
// Terran cruiser / carrier (docs/notes/mods.md "NPC ships": its own turret hardpoints as separate parts that die one by one,
// the hull standing until it is destroyed itself, escorts round it), an event's loaner swapped in flight and given back
// (docs/notes/mods.md "Event loaners"), and a tractor beam's "tractorColor" on the beam drawn (CombatRadar).

using System.Collections;
using System.IO;
using System.Linq;
using GoF2Remake.Data;
using GoF2Remake.Events;
using GoF2Remake.Flight;
using GoF2Remake.Modding;
using GoF2Remake.Multiplayer;
using GoF2Remake.World;
using NUnit.Framework;
using UnityEngine;
using UnityEngine.TestTools;

namespace GoF2Remake.Tests
{
    [Category("Smoke")]
    public class ModderRequestsFlightTests : PlayModeTest
    {
        const string Mod = "requests_test";

        static void WriteMod()
        {
            string dir = Path.Combine(ModManager.UserFolder, Mod);
            Directory.CreateDirectory(dir);
            File.WriteAllText(Path.Combine(dir, ModManifest.FileName), "{ \"id\": \"" + Mod + "\", \"name\": \"Test\", \"version\": \"1.0\", \"author\": \"Tests\" }");
            File.WriteAllText(Path.Combine(dir, "npcships.json"), @"[
                { ""id"": ""cruiser"", ""kind"": ""capital"", ""looksLike"": ""battleship"", ""name"": ""Test Cruiser"", ""race"": ""terran"",
                  ""hardpoints"": [ { ""position"": [0, 3200, 9000], ""item"": 48 }, { ""position"": [0, -3000, 4000], ""upsideDown"": true },
                                    { ""position"": [2000, 1500, 0] } ],
                  ""escorts"": { ""count"": 2 } } ]");
            File.WriteAllText(Path.Combine(dir, "items.json"),
                "[ { \"id\": \"beam\", \"base\": 68, \"name\": \"Orange Beam\", \"tractorColor\": \"#ff8000\", \"stats\": { \"tractorNoLock\": 1 } } ]");
            ModManager.Scan();
            ModManager.SetEnabled(Mod, true);
        }

        IEnumerator EnterSpace()
        {
            FreePlayAt(78);
            Session.Difficulty = Session.DifficultyNormal;
            WriteMod();
            yield return Load("Space");
            yield return WaitFor(() => Find<SpaceLevel>() != null && Find<SpaceLevel>().Player != null && Find<SpaceLevel>().Traffic != null, 60f, "the level");
        }

        [TearDown]
        public void ModOff() { ModManager.SetEnabled(Mod, false); }

        [OneTimeTearDown]
        public void MountedModsBack() { ModManager.Scan(); }

        [UnityTest]
        public IEnumerator ACapitalShipsTurretsAreSeparatePartsOfIt()
        {
            yield return EnterSpace();
            var level = Find<SpaceLevel>();
            var type = ModNpcShips.Get(Mod + ":cruiser");
            Assert.IsNotNull(type, "the mod's NPC ship type");
            Assert.AreEqual(3, type.hardpoints.Count);
            DebugSpawner.SpawnNpcType(level, type);
            NpcShip host = null;
            yield return WaitFor(() => (host = level.Traffic.Ships.Find(s => s != null && s.Spec.npcType == type && !s.Spec.capitalPart)) != null, 20f, "the cruiser");
            System.Collections.Generic.List<NpcShip> Turrets() =>
                level.Traffic.Ships.FindAll(s => s != null && s.Spec.capitalHost == host.Spec && s.IsTurret && !s.Gone && s.Target.Alive);
            yield return WaitFor(() => Turrets().Count == 3, 20f, "its three turrets");
            Assert.AreEqual("Test Cruiser", host.Spec.name);
            Assert.IsTrue(level.Traffic.Ships.Count(s => s != null && s.Spec.capitalHost == host.Spec && !s.IsTurret) >= 2, "its escorts");

            // A turret dies alone; the hull stands.
            var first = Turrets()[0];
            first.Target.Damage(first.Target.maxHp * 100f);
            yield return Run(0.5f);
            Assert.AreEqual(2, Turrets().Count);
            Assert.IsTrue(host.Target.Alive, "the hull stands without one turret");

            // The hull destroyed takes its turrets with it.
            host.Target.Damage(host.Target.maxHp * 100f);
            yield return WaitFor(() => Turrets().Count == 0, 20f, "the turrets gone with the hull");
            Assert.IsFalse(host.Target.Alive);
        }

        [UnityTest]
        public IEnumerator ALoanerInFlightAndTheOwnShipBack()
        {
            yield return EnterSpace();
            var level = Find<SpaceLevel>();
            int own = Session.ShipIndex;
            Assert.AreNotEqual(9, own);
            EventHost.Send(0, NetAdmin.Order.Lend, 9, 0, 0, "trial", "Test");
            yield return Run(0.3f);
            Assert.AreEqual(9, Session.ShipIndex, "the H'Soc on loan");
            Assert.AreEqual("trial", Session.ParkedShip?.lentBy);
            Assert.IsNotNull(level.Player.visualModel, "the loaner's model in flight");
            EventHost.Send(0, NetAdmin.Order.Lend, -1, 0, 0, "trial", "Test");
            yield return Run(0.3f);
            Assert.AreEqual(own, Session.ShipIndex);
            Assert.IsNull(Session.ParkedShip);
        }

        [UnityTest]
        public IEnumerator ATractorColorTintsTheBeam()
        {
            FreePlayAt(78);
            Session.Difficulty = Session.DifficultyNormal;
            WriteMod();
            int beam = ModContent.ItemIndexOf(Mod + ":beam");
            Assert.GreaterOrEqual(beam, 0);
            Session.Equipment.RemoveAll(e => Db.Item(e.item) is ItemData it && Shop.KindOf(it) == 13);
            Session.Equipment.Add(new ItemStack(beam, 1));
            yield return Load("Space");
            yield return WaitFor(() => Find<CombatRadar>() != null && Get<Transform>(Find<CombatRadar>(), "beam") != null, 60f, "the radar's beam");
            var radar = Find<CombatRadar>();
            var mats = Get<Transform>(radar, "beam").GetComponentsInChildren<Renderer>(true).SelectMany(r => r.sharedMaterials).Where(m => m != null && m.HasProperty("_Color")).ToList();
            Assert.IsNotEmpty(mats);
            foreach (var m in mats)
            {
                var c = m.GetColor("_Color");
                Assert.AreEqual(1f, c.r, 0.01f, m.name);
                Assert.AreEqual(128f / 255f, c.g, 0.01f, m.name);
                Assert.AreEqual(0f, c.b, 0.01f, m.name);
            }
            Assert.IsTrue(Get<bool>(radar, "tractorNoLock"));
        }
    }
}
