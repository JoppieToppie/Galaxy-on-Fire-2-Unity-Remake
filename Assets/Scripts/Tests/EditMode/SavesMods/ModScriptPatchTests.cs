// ModScriptPatchTests.cs
// Feature: docs/notes/mods.md "Mod scripts" > Patches (Core/PatchRuntime.cs, Editor/CodeGen/PatchWeaver.cs,
// Modding/ModScriptsPatch.cs): the build weaves a check into every method of the game's five runtime assemblies, so a mod's
// script can put a Lua function in the place of any of them (game.patch), on IL2CPP too. Expected: every assembly is
// woven; a patch gets what it replaced ("original"), the object, the arguments, and returns the result and the ref / out
// values; patches stack (the newest first) and come out again (game.off, the scripts stopping); the method's own exception
// reaches its caller unwrapped; a failing Lua patch leaves the method's own behaviour and is taken out after 5 errors in a
// row; a method that handles a file, a class scripts can't use and what the weaver leaves out are refused with the reason.

using System;
using System.Collections.Generic;
using System.Linq;
using System.Reflection;
using GoF2Remake.Data;
using GoF2Remake.Modding;
using GoF2Remake.Patching;
using NUnit.Framework;

namespace GoF2Remake.Tests
{
    public class ModScriptPatchTests : SavesModsTest
    {
        const string Id = "patch_mod";
        const BindingFlags Any = BindingFlags.Public | BindingFlags.NonPublic | BindingFlags.Static | BindingFlags.Instance | BindingFlags.DeclaredOnly;
        static readonly string[] GameAssemblies = { "GoF2.Core", "GoF2.Game", "GoF2.Space", "GoF2.Net", "GoF2.App" };

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

        void Run(string lua)
        {
            WriteMod("s", Id, "1.0", "", ("scripts/main.lua", lua));
            ScanAndEnable(Id);
            ModScripts.SetApproved(Installed(Id), true);
            ModScripts.Sync(true);
        }

        static string Out() => string.Join("|", ModScripts.Lines.Where(l => !l.error).Select(l => l.text));
        static string Errors() => string.Join("|", ModScripts.Lines.Where(l => l.error).Select(l => l.text));
        static Assembly Game(string name) => AppDomain.CurrentDomain.GetAssemblies().First(a => a.GetName().Name == name);
        static MethodInfo Method(Type type, string name) => type.GetMethods(Any).Single(m => m.Name == name);

        // ---- the weaving ------------------------------------------------------------------------------------------------

        [Test]
        public void EveryGameAssemblyIsWoven()
        {
            int total = 0;
            foreach (string name in GameAssemblies)
            {
                var (sites, skipped) = PatchRuntime.Woven(Game(name));
                Assert.Greater(sites, 300, name + ": the build step (PatchWeaver) ran");
                Assert.Greater(skipped, 0, name);
                total += sites;
            }
            Assert.Greater(total, 12000, "the game's named methods are patchable");
        }

        [Test]
        public void AnOverridableMethodHasItsBaseCall()
        {
            int found = 0;
            foreach (string name in GameAssemblies)
                foreach (var t in Game(name).GetTypes())
                    foreach (var m in t.GetMethods(Any))
                    {
                        if (m.GetCustomAttribute<PatchSiteAttribute>(false) == null || !m.IsVirtual || m.IsFinal || t.IsSealed) continue;
                        var call = t.GetMethod(PatchRuntime.BasePrefix + m.Name, BindingFlags.NonPublic | BindingFlags.Instance | BindingFlags.DeclaredOnly, null,
                                               m.GetParameters().Select(p => p.ParameterType).ToArray(), null);
                        Assert.IsNotNull(call, t.FullName + "." + m.Name);
                        Assert.AreEqual(m.ReturnType, call.ReturnType);
                        found++;
                    }
            Assert.Greater(found, 10);
        }

        [Test]
        public void WhatIsLeftOutSaysWhy()
        {
            Assert.IsTrue(PatchRuntime.CanPatch(Method(typeof(Shop), "StationStocks"), out _));
            Assert.IsFalse(PatchRuntime.CanPatch(typeof(string).GetMethod("Trim", Type.EmptyTypes), out string why));
            StringAssert.Contains("only the game's own classes", why);
            Assert.IsFalse(PatchRuntime.CanPatch(Method(typeof(PatchRuntime), "Skip"), out _), "the patch runtime itself");
            Assert.Throws<ArgumentException>(() => PatchRuntime.Add(typeof(string).GetMethod("Trim", Type.EmptyTypes), (o, s, a) => null));
        }

        // ---- patches from C# --------------------------------------------------------------------------------------------

        [Test]
        public void AStaticMethodIsReplacedAndPutBack()
        {
            Assert.IsFalse(Shop.StationStocks(3, 7), "a tech 3 station doesn't stock tech 7");
            var patch = PatchRuntime.Add(Method(typeof(Shop), "StationStocks"), (original, self, args) =>
            {
                Assert.IsNull(self);
                return (int)args[1] == 7 || (bool)original(self, args);
            });
            Assert.IsTrue(Shop.StationStocks(3, 7));
            Assert.IsFalse(Shop.StationStocks(3, 8), "the method's own answer through original");
            Assert.IsTrue(Shop.StationStocks(8, 3));
            patch.Dispose();
            Assert.IsFalse(Shop.StationStocks(3, 7));
        }

        [Test]
        public void PatchesStackTheNewestFirst()
        {
            var m = Method(typeof(Achievements), "Threshold");
            int own = Achievements.Threshold(5, 1);
            var first = PatchRuntime.Add(m, (original, self, args) => (int)original(self, args) + 1);
            var second = PatchRuntime.Add(m, (original, self, args) => (int)original(self, args) * 10);
            Assert.AreEqual((own + 1) * 10, Achievements.Threshold(5, 1));
            first.Dispose();
            Assert.AreEqual(own * 10, Achievements.Threshold(5, 1));
            second.Dispose();
            Assert.AreEqual(own, Achievements.Threshold(5, 1));
        }

        [Test]
        public void AnOutValueGoesBack()
        {
            Assert.IsFalse(ModContent.TryResolveItem("no_mod:nothing", out _));
            using (PatchRuntime.Add(Method(typeof(ModContent), "TryResolveItem"), (original, self, args) =>
            {
                if ((string)args[0] != "no_mod:nothing") return original(self, args);
                args[1] = 42;
                return true;
            }))
            {
                Assert.IsTrue(ModContent.TryResolveItem("no_mod:nothing", out int index));
                Assert.AreEqual(42, index);
                Assert.IsTrue(ModContent.TryResolveItem("5", out index), "the original's out value comes back too");
                Assert.AreEqual(5, index);
            }
        }

        [Test]
        public void AnInstanceMethodGetsItsObject()
        {
            var item = Database.Shared.Item(5);
            using (PatchRuntime.Add(Method(typeof(ItemData), "HasAttr"), (original, self, args) => ReferenceEquals(self, item) && (int)args[0] == 9999))
            {
                Assert.IsTrue(item.HasAttr(9999));
                Assert.IsFalse(Database.Shared.Item(6).HasAttr(9999), "another object");
            }
            Assert.IsFalse(item.HasAttr(9999));
        }

        [Test]
        public void TheMethodsOwnExceptionReachesTheCaller()
        {
            using (PatchRuntime.Add(Method(typeof(Achievements), "Threshold"), (original, self, args) => original(self, args)))
                Assert.Throws<IndexOutOfRangeException>(() => Achievements.Threshold(9999, 1), "not wrapped by the reflection call");
        }

        [Test]
        public void AMissingValueIsTheTypesDefault()
        {
            using (PatchRuntime.Add(Method(typeof(Shop), "StationStocks"), (original, self, args) => null))
                Assert.IsFalse(Shop.StationStocks(8, 3));
        }

        // ---- patches from Lua -------------------------------------------------------------------------------------------

        [Test]
        public void AScriptPatchesAStaticMethod()
        {
            Run(@"h = game.patch('Shop', 'StationStocks', function(original, stationTech, itemTech)
                      return itemTech <= 5 and original(stationTech, itemTech)
                  end)
                  game.on('undo', function() print(game.off(h)) end)");
            Assert.AreEqual("", Errors());
            Assert.IsTrue(Shop.StationStocks(8, 3));
            Assert.IsFalse(Shop.StationStocks(8, 7), "the script's rule");
            Assert.IsFalse(Shop.StationStocks(2, 3), "the game's rule through original");
            ModScripts.Raise("undo");
            Assert.AreEqual("true", Out());
            Assert.IsTrue(Shop.StationStocks(8, 7), "game.off takes it out");
        }

        [Test]
        public void AScriptChangesWhatAShopStocks()
        {
            // #313: a game mode as a mod (only gear up to a tech level in the shops) without a hook made for it.
            var db = Database.Load();
            Assert.IsTrue(Enumerable.Range(0, 30).Any(_ => Shop.GenerateItems(db, 70).Exists(s => db.Item(s.item).techLevel > 2)), "station 70 stocks tech 3+");
            Run(@"game.patch('Shop', 'GenerateItems', function(original, db, station)
                      local list = original(db, station)
                      for i = list.Count - 1, 0, -1 do
                          if db:Item(list[i].item).techLevel > 2 then list:RemoveAt(i) end
                      end
                      return list
                  end)");
            Assert.AreEqual("", Errors());
            for (int i = 0; i < 10; i++)
                Assert.IsTrue(Shop.GenerateItems(db, 70).TrueForAll(s => db.Item(s.item).techLevel <= 2), Errors());
            Assert.AreEqual("", Errors());
            ModScripts.StopAll();
            Assert.IsTrue(Enumerable.Range(0, 30).Any(_ => Shop.GenerateItems(db, 70).Exists(s => db.Item(s.item).techLevel > 2)), "stopping the scripts puts the method back");
        }

        [Test]
        public void AScriptPatchesAnObjectsMethodAndAPrivateOne()
        {
            Run(@"game.patch('ItemData', 'HasAttr', function(original, self, id)
                      if id == 9999 then return self.index == 5 end
                      return original(self, id)
                  end)
                  game.patch('Achievements', 'Strict', function(original, medal) return medal == 1 end)");
            Assert.AreEqual("", Errors());
            Assert.IsTrue(Database.Shared.Item(5).HasAttr(9999));
            Assert.IsFalse(Database.Shared.Item(6).HasAttr(9999));
            var strict = typeof(Achievements).GetMethod("Strict", BindingFlags.NonPublic | BindingFlags.Static);
            Assert.IsTrue((bool)strict.Invoke(null, new object[] { 1 }), "a private method");
            Assert.IsFalse((bool)strict.Invoke(null, new object[] { 5 }));
        }

        [Test]
        public void AScriptReturnsAnOutValue()
        {
            Run(@"game.patch('ModContent', 'TryResolveItem', function(original, reference)
                      if reference == 'magic' then return true, 7 end
                      local ok, index = original(reference)
                      print(reference, ok, index)
                      return ok, index
                  end)");
            Assert.AreEqual("", Errors());
            Assert.IsTrue(ModContent.TryResolveItem("magic", out int index));
            Assert.AreEqual(7, index);
            Assert.IsTrue(ModContent.TryResolveItem("5", out index));
            Assert.AreEqual(5, index);
            StringAssert.Contains("5\ttrue\t5", Out());
        }

        [Test]
        public void AFailingPatchLeavesTheMethodAndIsTakenOut()
        {
            Run("game.patch('Shop', 'StationStocks', function(original, a, b) error('boom') end)");
            for (int i = 0; i < 4; i++) Assert.IsTrue(Shop.StationStocks(8, 3), "the method's own answer");
            Assert.AreEqual(4, ModScripts.Lines.Count(l => l.error && l.text.Contains("boom")));
            Assert.IsTrue(Shop.StationStocks(8, 3));
            StringAssert.Contains("taken out after 5 errors", Errors());
            int lines = ModScripts.Lines.Count;
            Assert.IsTrue(Shop.StationStocks(8, 3));
            Assert.AreEqual(lines, ModScripts.Lines.Count, "gone: no more errors");
        }

        [Test]
        public void TheMethodsOwnExceptionPassesThroughAScript()
        {
            Run("game.patch('Achievements', 'Threshold', function(original, medal, grade) return original(medal, grade) end)");
            Assert.AreEqual(Achievements.Threshold(5, 1), Achievements.Threshold(5, 1));
            Assert.Throws<IndexOutOfRangeException>(() => Achievements.Threshold(9999, 1));
        }

        [Test]
        public void WhatScriptsMayNotPatchIsRefused()
        {
            Run(@"local function try(...) local ok, e = pcall(game.patch, ...) print(ok and 'patched' or e) end
                  try('SaveGame', 'PathOf', function() end)
                  try('GamePaths', 'Data', function() end)
                  try('Shop', 'NoSuchMethod', function() end)
                  try('CapitalShips', 'RollLoot', function() end)
                  try('CapitalShips', 'RollLoot', function(original, db) return original(db) end, 'Database')");
            var lines = ModScripts.Lines.Where(l => !l.error).Select(l => l.text).ToList();
            Assert.AreEqual(5, lines.Count, Out() + Errors());
            StringAssert.Contains("handles a file or address", lines[0]);
            StringAssert.Contains("GamePaths", lines[1]);
            StringAssert.Contains("no method \"NoSuchMethod\"", lines[2]);
            StringAssert.Contains("overloads", lines[3]);
            Assert.AreEqual("patched", lines[4]);
            Assert.IsNotNull(GoF2Remake.World.CapitalShips.RollLoot(Database.Shared), "the patched overload still answers");
            Assert.AreEqual("", Errors());
        }
    }
}
