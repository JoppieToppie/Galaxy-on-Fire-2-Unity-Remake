// PatchExampleModsTests.cs
// Feature: the small script examples in Modding/Examples made from players' requests (docs/notes/mods.md "Patches"), each a
// few lines of Lua on game.patch, and what the Lua bridge needed for them.
//   mixed_wings          GitHub #307: every raider of a group gets its own ship (the game, like the original, picks one
//                        model a group, TrafficPlan.AddRaiders).
//   heavy_ammo           GitHub #323: mounted secondary weapons take cargo space, half a ton a round (Shop.CargoLoad).
//   khador_interference  GitHub #325: no Khador Drive in the systems data/interference.json names, Loma (system 25) to
//                        begin with (GalaxyMap.HasJumpDrive).
//   (turret_status, the HUD label of #314, needs a flight: PlayMode/TurretStatusExampleTests.)
// The bridge: a class that hides a base member with "new" can be used (UI Toolkit's Label: MoonSharp refused it), a Lua
// function is a Predicate / Comparison / Action of the game's classes (list:Find(fn)), and game.style sets an interface
// element's inline styles (its style object is an internal Unity class scripts can't hold).

using System;
using System.Collections.Generic;
using System.Linq;
using System.Reflection;
using GoF2Remake.Data;
using GoF2Remake.Flight;
using GoF2Remake.Modding;
using GoF2Remake.Patching;
using NUnit.Framework;
using UnityEngine;

namespace GoF2Remake.Tests
{
    public class PatchExampleModsTests : SavesModsTest
    {
        [SetUp]
        public void OwnApprovals()
        {
            ModScripts.StopAll();
            ModScripts.ClearLines();
            ModScripts.ApprovedOverride = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
            PatchRuntime.RemoveAll();
        }

        [TearDown]
        public void StopScripts()
        {
            ModScripts.StopAll();
            ModScripts.ClearLines();
            ModScripts.ApprovedOverride = null;
            PatchRuntime.RemoveAll();
        }

        static string Out() => string.Join("|", ModScripts.Lines.Where(l => !l.error).Select(l => l.text));
        static string Errors() => string.Join("|", ModScripts.Lines.Where(l => l.error).Select(l => l.text));

        void Example(string id)
        {
            CopyExample(id);
            ScanAndEnable(id);
            var mod = Installed(id);
            Assert.IsFalse(mod.Broken, AllMessages(mod));
            Assert.IsEmpty(mod.Warnings, AllMessages(mod));
            ModScripts.SetApproved(mod, true);
            ModScripts.Sync(true);
            Assert.AreEqual("", Errors());
        }

        void Run(string lua)
        {
            WriteMod("s", "bridge_mod", "1.0", "", ("scripts/main.lua", lua));
            ScanAndEnable("bridge_mod");
            ModScripts.SetApproved(Installed("bridge_mod"), true);
            ModScripts.Sync(true);
        }

        static List<SpawnSpec> Raiders(int count)
        {
            var list = new List<SpawnSpec>();
            typeof(TrafficPlan).GetMethod("AddRaiders", BindingFlags.NonPublic | BindingFlags.Static).Invoke(null, new object[] { list, count, Standing.Pirate, Vector3.zero });
            return list;
        }

        [Test]
        public void MixedWingsGivesEveryRaiderItsOwnShip()
        {
            Assert.AreEqual(1, Raiders(30).Select(s => s.ship).Distinct().Count(), "the game's rule: one model a group");
            Example("mixed_wings");
            var group = Raiders(30);
            Assert.AreEqual(30, group.Count);
            Assert.Greater(group.Select(s => s.ship).Distinct().Count(), 1);
            Assert.IsTrue(group.All(s => s.race == Standing.Pirate && s.group == NpcGroup.Raider), "still the game's raiders");
            Assert.AreEqual("", Errors());
        }

        [Test]
        public void HeavyAmmoTakesCargoSpace()
        {
            Session.ResetNewGame();
            Example("heavy_ammo");
            var db = Database.Load();
            int load = Shop.CargoLoad(), free = Shop.FreeCargo(db);
            Session.Equipment.Add(new ItemStack(41, 10));   // 10 EMP GL I
            Assert.AreEqual(load + 5, Shop.CargoLoad());
            Assert.AreEqual(free - 5, Shop.FreeCargo(db), "less room for cargo");
            Session.Equipment.Add(new ItemStack(16, 1));    // a primary weapon weighs nothing
            Assert.AreEqual(load + 5, Shop.CargoLoad());
            Assert.AreEqual("", Errors());
        }

        [Test]
        public void KhadorInterferenceSwitchesTheDriveOffInLoma()
        {
            Session.ResetNewGame();
            Example("khador_interference");
            var db = Database.Load();
            Session.Equipment.Add(new ItemStack(85, 1));   // the Khador Drive
            Session.StationIndex = db.Stations.First(s => s.system == 25).index;
            Assert.IsFalse(GalaxyMap.HasJumpDrive(db), "Loma");
            Session.StationIndex = 70;
            Assert.IsTrue(GalaxyMap.HasJumpDrive(db), "anywhere else");
            Assert.AreEqual("", Errors());
            ModScripts.StopAll();
            Session.StationIndex = db.Stations.First(s => s.system == 25).index;
            Assert.IsTrue(GalaxyMap.HasJumpDrive(db), "the mod off: the drive works in Loma");
        }

        // ---- the bridge -------------------------------------------------------------------------------------------------

        [Test]
        public void AScriptBuildsAndStylesInterface()
        {
            Run(@"local root = VisualElement.__new()
                  made = Label.__new('AUTO TURRET: ON')
                  root:Add(made)
                  game.style(made, { position = 'absolute', top = 40, left = '5%', color = '#ff8800', fontSize = 18, opacity = 0.5, display = 'none' })
                  print(root.childCount, made.text, made.parent == root)
                  print(pcall(game.style, made, { position = 'sideways' }))
                  print(pcall(game.style, made, { nothing = 1 }))
                  game.style(made, { top = nil, display = 'flex' })");
            Assert.AreEqual("", Errors());
            var lines = ModScripts.Lines.Select(l => l.text).ToList();
            Assert.AreEqual("1\tAUTO TURRET: ON\ttrue", lines[0], "a Label (it hides VisualElement.experimental) can be made");
            StringAssert.Contains("position is one of Relative, Absolute", lines[1]);
            StringAssert.Contains("there is no style \"nothing\"", lines[2]);
        }

        [Test]
        public void ALuaFunctionSearchesAndSortsAGameList()
        {
            Run(@"local db = Database.Shared
                  print(db.Stations:Find(function(s) return s.index == 70 end).index)
                  local stock = Shop.GenerateItems(db, 70)
                  stock:RemoveAll(function(s) return s.item ~= stock[0].item end)
                  print(stock.Count)");
            Assert.AreEqual("", Errors());
            Assert.AreEqual("70|1", Out());
        }
    }
}
