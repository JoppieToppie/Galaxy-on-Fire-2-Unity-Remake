// ModScriptTests.cs
// Feature: docs/notes/mods.md "Mod scripts" (ModScripts.cs, ModScriptsApi.cs, ModScriptsSandbox.cs): a mod's Lua files in
// scripts/ run in one Lua state per mod once the player allowed them: every file directly in scripts/ at start (main.lua
// first), subfolders by require; the global "game" (events, timers, the save's table, hooks, classes); the sandbox (the
// game's and Unity's scene classes, no .NET, no files); errors and endless loops as the mod's warnings.

using System.Collections.Generic;
using System.Linq;
using GoF2Remake.Data;
using GoF2Remake.Modding;
using NUnit.Framework;

namespace GoF2Remake.Tests
{
    public class ModScriptTests : SavesModsTest
    {
        const string Id = "script_mod";

        [SetUp]
        public void OwnApprovals()
        {
            ModScripts.StopAll();
            ModScripts.ClearLines();
            ModScripts.ApprovedOverride = new HashSet<string>(System.StringComparer.OrdinalIgnoreCase);
        }

        [TearDown]
        public void StopScripts()
        {
            ModScripts.StopAll();
            ModScripts.ClearLines();
            ModScripts.ApprovedOverride = null;
        }

        /// <summary>The mod written, turned on, allowed and started.</summary>
        ModInfo Run(params (string path, string text)[] files)
        {
            WriteMod("s", Id, "1.0", "", files);
            ScanAndEnable(Id);
            ModScripts.SetApproved(Installed(Id), true);
            ModScripts.Sync(true);
            return Installed(Id);
        }

        static string Out() => string.Join("|", ModScripts.Lines.Where(l => !l.error).Select(l => l.text));
        static string Errors() => string.Join("|", ModScripts.Lines.Where(l => l.error).Select(l => l.text));

        [Test]
        public void TopLevelFilesRunMainFirstAndSubfoldersOnlyByRequire()
        {
            var mod = Run(("scripts/zeta.lua", "print('zeta')"), ("scripts/main.lua", "print('main')"), ("scripts/alpha.lua", "print('alpha')"),
                ("scripts/lib/util.lua", "print('util') return { twice = function(x) return x * 2 end }"));
            Assert.AreEqual("", Errors());
            Assert.AreEqual("main|alpha|zeta", Out());
            CollectionAssert.AreEqual(new[] { "scripts/main.lua", "scripts/alpha.lua", "scripts/zeta.lua" }, ModScripts.StartFiles(mod));
            Assert.AreEqual(4, ModScripts.Files(mod).Count);
            Assert.IsTrue(ModScripts.HasScripts(mod));
        }

        [Test]
        public void RequireLoadsAModuleOnce()
        {
            Run(("scripts/main.lua", "local u = require('lib.util') local v = require('lib.util') print(u.twice(21), u == v) require('other')"),
                ("scripts/other.lua", "print('other')"),
                ("scripts/lib/util.lua", "print('util') return { twice = function(x) return x * 2 end }"));
            Assert.AreEqual("", Errors());
            Assert.AreEqual("util|42\ttrue|other", Out(), "other.lua ran through require and not again as a start file");
        }

        [Test]
        public void AMissingModuleIsAWarningOfTheMod()
        {
            var mod = Run(("scripts/main.lua", "require('nothing')"));
            StringAssert.Contains("nothing", Errors());
            Assert.IsTrue(mod.Warnings.Any(w => w.StartsWith("Script: ")), AllMessages(mod));
            Assert.IsFalse(mod.Broken);
        }

        [Test]
        public void EventsReachTheHandlersUntilTheyAreTakenOff()
        {
            Run(("scripts/main.lua", "local h h = game.on('ping', function(a, b) print('ping', a, b) if a == 2 then game.off(h) end end)"));
            ModScripts.Raise("ping", 1, "x");
            ModScripts.Raise("ping", 2, true);
            ModScripts.Raise("ping", 3, null);
            ModScripts.Raise("pong", 4);
            Assert.AreEqual("", Errors());
            Assert.AreEqual("ping\t1\tx|ping\t2\ttrue", Out());
        }

        [Test]
        public void TimersFollowTheGameTime()
        {
            Run(("scripts/main.lua", "game.after(1, function() print('once') end) local n = 0 local t t = game.every(0.5, function() n = n + 1 print('tick', n) if n == 3 then game.off(t) end end) game.on('update', function(dt) if dt == 0 then print('paused') end end)"));
            ModScripts.Tick(0.4f);
            Assert.AreEqual("", Out());
            ModScripts.Tick(0.2f);
            Assert.AreEqual("tick\t1", Out());
            ModScripts.Tick(0f);
            Assert.AreEqual("tick\t1|paused", Out(), "a paused frame moves no timer");
            ModScripts.Tick(0.5f);
            ModScripts.Tick(0.5f);
            ModScripts.Tick(0.5f);
            ModScripts.Tick(0.5f);
            Assert.AreEqual("", Errors());
            Assert.AreEqual("tick\t1|paused|once|tick\t2|tick\t3", Out());
        }

        [Test]
        public void ScriptsReadAndChangeTheGame()
        {
            Session.Credits = 1000;
            Run(("scripts/main.lua", "Session.Credits = Session.Credits + 234 local v = Vector3.__new(1, 2, 3) + Vector3.up print(v.y, Mathf.Max(3, 7), game.type('GoF2Remake.Data.Session') == Session, game.mod.id, game.multiplayer())"));
            Assert.AreEqual("", Errors());
            Assert.AreEqual(1234, Session.Credits);
            Assert.AreEqual("3\t7\ttrue\tscript_mod\tnone", Out());
        }

        [Test]
        public void TheSandboxHasNoFilesNoDotNetAndNoDoorsOfTheGame()
        {
            Run(("scripts/main.lua", "print(io == nil, os.execute == nil, os.remove == nil, debug == nil, not pcall(dofile, 'C:/x.lua'))"),
                ("scripts/a.lua", "print(pcall(import, 'System.IO.File'))"),
                ("scripts/b.lua", "print(pcall(import, 'GamePaths'))"),
                ("scripts/c.lua", "print(pcall(import, 'UnityEngine.Application'))"),
                ("scripts/d.lua", "print(pcall(import, 'ModScripts'))"),
                ("scripts/e.lua", "local mods = import('ModManager').Installed local m = game.get(mods, 0) print(m.Id, pcall(function() return m:LocalFile('mod.json') end))"),
                ("scripts/f.lua", "print(pcall(function() return Session:GetType() end))"));
            Assert.AreEqual("", Errors());
            var lines = ModScripts.Lines.Select(l => l.text).ToList();
            Assert.AreEqual(7, lines.Count, Out());
            Assert.AreEqual("true\ttrue\ttrue\ttrue\ttrue", lines[0]);
            for (int i = 1; i <= 4; i++) StringAssert.StartsWith("false\t", lines[i], lines[i]);
            StringAssert.StartsWith("script_mod\tfalse", lines[5], "a game method taking a file name is left out");
            StringAssert.StartsWith("false", lines[6], "no System.Type, so no reflection");
        }

        [Test]
        public void EveryDeniedGameClassStillExists()
        {
            var names = new HashSet<string>(new[] { "GoF2.Core", "GoF2.Game", "GoF2.Space", "GoF2.Net", "GoF2.App" }
                .SelectMany(a => System.AppDomain.CurrentDomain.GetAssemblies().First(x => x.GetName().Name == a).GetTypes()).Select(t => t.Name));
            var gone = ScriptAccess.DeniedGameNames.Where(n => !names.Contains(n)).ToList();
            Assert.IsEmpty(gone, "ScriptAccess.GameDenied names classes the game no longer has (renamed?): " + string.Join(", ", gone));
        }

        [Test]
        public void AnEndlessLoopIsStoppedAndTheGameGoesOn()
        {
            var mod = Run(("scripts/main.lua", "game.on('spin', function() while true do end end) game.on('spin', function() print('second') end)"));
            ModScripts.Raise("spin");
            StringAssert.Contains("too long", Errors());
            Assert.AreEqual("second", Out(), "the other handlers still run");
            Assert.IsTrue(mod.Warnings.Any(w => w.Contains("too long")));
        }

        [Test]
        public void HandlersReuseTheirCoroutinesInsteadOfAllocatingNewStacks()
        {
            // Discord, 2026-11-27: twelve idle update handlers stalled the game (each call made a ~2 MB coroutine)
            Run(("scripts/main.lua", "n = 0 for i = 1, 12 do game.on('update', function(dt) n = n + 1 end) end "
                + "game.on('spin', function() while true do end end) game.on('bad', function() error('boom') end) game.on('show', function() print(n) end)"));
            ModScripts.Tick(0.016f);
            ModScripts.Raise("spin");   // a stopped and a failed call aren't reused, the next calls still run
            ModScripts.Raise("bad");
            long before = System.GC.GetAllocatedBytesForCurrentThread();
            for (int i = 0; i < 100; i++) ModScripts.Tick(0.016f);
            long perFrame = (System.GC.GetAllocatedBytesForCurrentThread() - before) / 100;
            ModScripts.Raise("show");
            Assert.AreEqual("1212", Out(), "every handler ran every frame");
            Assert.Less(perFrame, 100_000, "12 handlers a frame allocated " + perFrame + " bytes");
        }

        [Test]
        public void AFailingHandlerIsDroppedAfterFiveFailures()
        {
            Run(("scripts/main.lua", "game.on('bad', function() error('boom') end) game.on('bad', function() print('ok') end)"));
            for (int i = 0; i < 8; i++) ModScripts.Raise("bad");
            Assert.AreEqual(8, ModScripts.Lines.Count(l => !l.error && l.text == "ok"));
            Assert.AreEqual(5, ModScripts.Lines.Count(l => l.error && l.text.Contains("boom")));
            StringAssert.Contains("removed", Errors());
        }

        [Test]
        public void ScriptsRunOnlyOnceThePlayerAllowedThemAndAskAgainWhenTheyChange()
        {
            string dir = WriteMod("s", Id, "1.0", "", ("scripts/main.lua", "print('ran')"));
            ScanAndEnable(Id);
            ModScripts.Sync(true);
            Assert.AreEqual("", Out(), "on but not allowed: the scripts don't run");
            Assert.IsFalse(ModScripts.AnyRunning);
            CollectionAssert.AreEqual(new[] { Id }, ModScripts.Unapproved(ModManager.UserMods).Select(m => m.Id).ToArray());

            ModScripts.SetApproved(Installed(Id), true);
            ModScripts.Sync();
            Assert.AreEqual("ran", Out());
            Assert.IsEmpty(ModScripts.Unapproved(ModManager.UserMods));

            System.IO.File.WriteAllText(System.IO.Path.Combine(dir, "scripts", "main.lua"), "print('changed')");
            ModManager.Scan();
            ModScripts.Sync();
            Assert.AreEqual("ran", Out(), "other script files: not allowed yet");
            Assert.IsFalse(ModScripts.AnyRunning);
            Assert.IsFalse(ModScripts.IsApproved(Installed(Id)));

            ModScripts.SetApproved(Installed(Id), true);
            ModScripts.Sync();
            Assert.AreEqual("ran|changed", Out());
        }

        [Test]
        public void TurningTheModOffStopsItsScripts()
        {
            Run(("scripts/main.lua", "game.on('ping', function() print('ping') end) game.on('stop', function() print('bye') end)"));
            ModScripts.Raise("ping");
            ModManager.SetEnabled(Id, false);
            ModScripts.Raise("ping");
            Assert.AreEqual("ping|bye", Out());
            Assert.IsFalse(ModScripts.AnyRunning);
        }

        [Test]
        public void SavedIsKeptInTheSaveData()
        {
            Run(("scripts/main.lua", "game.on('count', function() game.saved.n = (game.saved.n or 0) + 1 game.saved.list = { 'a', 'b' } game.saved.fn = print end) game.on('show', function() print(game.saved.n, game.saved.list and #game.saved.list) end)"));
            ModScripts.Raise("count");
            ModScripts.Raise("count");
            ModScripts.CaptureSaved();
            Assert.AreEqual("", Errors());
            Assert.IsTrue(Session.ScriptData.ContainsKey(Id));
            string json = Session.ScriptData[Id];
            StringAssert.Contains("\"n\":2", json.Replace(" ", ""));

            Session.ResetNewGame();
            ModScripts.Raise("show");
            Session.ScriptData[Id] = json;
            ModScripts.RestoreSaved(true);
            ModScripts.Raise("show");
            Assert.AreEqual("", Errors());
            Assert.AreEqual("nil\tnil|2\t2", Out(), "a new game starts empty, a loaded save brings its table");
        }

        [Test]
        public void TheDatabaseEventChangesTheTables()
        {
            int before = Database.Load().Item(0).maxPrice;
            Run(("scripts/main.lua", "game.on('database', function(db) local item = db:Item(0) item.maxPrice = item.maxPrice + 5 end)"));
            Assert.AreEqual("", Errors());
            Assert.AreEqual(before + 5, Database.Load().Item(0).maxPrice);
            Assert.AreEqual("", Errors());
            ModScripts.StopAll();
            Assert.AreEqual(before, Database.Load().Item(0).maxPrice);
        }

        [Test]
        public void AHookWrapsADelegateOfTheGameAndIsPutBack()
        {
            var said = new List<string>();
            var was = GameHooks.Notice;
            try
            {
                System.Func<string, bool> own = text => { said.Add(text); return true; };
                GameHooks.Notice = own;
                Run(("scripts/main.lua", "game.hook('GameHooks', 'Notice', function(original, text) return original('[' .. text .. ']') end) game.message('hello')"));
                Assert.AreEqual("", Errors());
                Assert.IsEmpty(said, "a message waits for the next frame (the scene's interface may still be building)");
                ModScripts.Tick(0.016f);
                CollectionAssert.AreEqual(new[] { "[hello]" }, said);
                StringAssert.Contains("[message] hello", Out(), "the console has it too");
                GameHooks.Notice("direct");
                Assert.AreEqual("[direct]", said[1], "the game's own calls go through the script too");
                ModScripts.StopAll();
                Assert.AreSame(own, GameHooks.Notice, "stopping puts the game's delegate back");
            }
            finally { GameHooks.Notice = was; }
        }

        [Test]
        public void ListenAddsToAnEventOfTheGame()
        {
            Run(("scripts/main.lua", "game.listen('ModManager', 'Changed', function() print('mods changed') end)"));
            Assert.AreEqual("", Errors());
            ModScripts.ClearLines();
            WriteMod("t", "other_mod");
            ModManager.Scan();
            StringAssert.Contains("mods changed", Out());
            ModScripts.Sync();
            Assert.IsTrue(ModScripts.AnyRunning, "a rescan with the same script files leaves them running");
            ModScripts.Raise("fail");
            ModScripts.StopAll();
            ModScripts.ClearLines();
            ModManager.Scan();
            Assert.AreEqual("", Out(), "stopped: no longer listening");
        }

        [Test]
        public void ARescanKeepsTheScriptsRunningAndTheirWarningsOnTheNewEntry()
        {
            Run(("scripts/main.lua", "n = 0 game.on('count', function() n = n + 1 print(n) end) error('at start')"));
            ModScripts.Raise("count");
            ModManager.Scan();
            ModScripts.Raise("count");
            Assert.AreEqual("1|2", Out(), "the same Lua state went on");
            Assert.IsTrue(Installed(Id).Warnings.Any(w => w.Contains("at start")), AllMessages(Installed(Id)));
        }

        [Test]
        public void CollectionsOfTheGameAreReadAndWritten()
        {
            Run(("scripts/main.lua", "game.set(Session.ScriptData, 'k', 'v') print(game.get(Session.ScriptData, 'k'), game.get(Session.ScriptData, 'none'), game.table(Session.ScriptData).k, Session.ScriptData.Count)"));
            Assert.AreEqual("", Errors());
            Assert.AreEqual("v\tnil\tv\t1", Out());
            Assert.AreEqual("v", Session.ScriptData["k"]);
        }

        [Test]
        public void EmitReachesTheOtherMods()
        {
            WriteMod("o", "other_mod", "1.0", "", ("scripts/main.lua", "game.on('hello', function(who, n) print('got', who, n) end)"));
            WriteMod("s", Id, "1.0", "", ("scripts/main.lua", "game.on('go', function() game.emit('hello', game.mod.id, 7) end)"));
            ScanAndEnable("other_mod", Id);
            ModScripts.SetApproved(Installed("other_mod"), true);
            ModScripts.SetApproved(Installed(Id), true);
            ModScripts.Sync(true);
            ModScripts.Raise("go");
            Assert.AreEqual("", Errors());
            Assert.AreEqual("got\tscript_mod\t7", Out());
            CollectionAssert.AreEqual(new[] { "other_mod", Id }, ModScripts.RunningIds.ToArray());
        }

        [Test]
        public void TheBuilderProjectKeepsItsScriptFiles()
        {
            Assert.AreEqual("lib/my_util.lua", ModProject.ScriptName(" Lib\\My Util.lua "));
            Assert.AreEqual("a_b.lua", ModProject.ScriptName("a.b"), "no dots: require reads them as folders");
            Assert.IsNull(ModProject.ScriptName(" /../ "));

            var p = ModProject.Create("Script project", "tests");
            Assert.IsEmpty(p.ScriptFiles());
            Assert.AreEqual("main.lua", p.AddScript("", out _), "the first script is main.lua");
            Assert.AreEqual(ModProject.ScriptTemplate, p.ReadScript("main.lua"));
            Assert.IsNull(ModScripts.SyntaxError(ModProject.ScriptTemplate, "main.lua"), "the template is valid Lua");
            Assert.AreEqual("zeta.lua", p.AddScript("Zeta", out _));
            Assert.AreEqual("lib/util.lua", p.AddScript("lib/util", out _));
            Assert.IsNull(ModScripts.SyntaxError(p.ReadScript("lib/util.lua"), "util.lua"));
            Assert.IsNull(p.AddScript("ZETA", out string taken));
            StringAssert.Contains("already", taken);
            CollectionAssert.AreEqual(new[] { "main.lua", "zeta.lua", "lib/util.lua" }, p.ScriptFiles(), "main first, start files, then modules");

            Assert.IsTrue(p.WriteScript("zeta.lua", "print('z')"));
            Assert.IsFalse(p.WriteScript("zeta.lua", "print('z')"), "the same text writes nothing");
            Assert.IsFalse(p.WriteScript("../outside.lua", "x"), "never outside scripts/");
            Assert.AreEqual("lib/alpha.lua", p.RenameScript("zeta.lua", "lib/alpha", out _));
            Assert.AreEqual("print('z')", p.ReadScript("lib/alpha.lua"));
            p.RemoveScript("lib/alpha.lua");
            p.RemoveScript("lib/util.lua");
            CollectionAssert.AreEqual(new[] { "main.lua" }, p.ScriptFiles());
            Assert.IsFalse(System.IO.Directory.Exists(System.IO.Path.Combine(p.Folder, "scripts", "lib")), "an emptied folder goes");

            // The project read as a mod (what the builder's game runs): its scripts need nobody's yes.
            ModManager.SetBuilderProject(p.Folder);
            try
            {
                Assert.IsTrue(ModScripts.HasScripts(ModManager.BuilderProject));
                Assert.IsTrue(ModScripts.IsApproved(ModManager.BuilderProject));
                ModScripts.Sync(true);
                StringAssert.Contains("Hello from Script project", Out());
                Assert.AreEqual("", Errors());
            }
            finally { ModScripts.StopAll(); ModManager.SetBuilderProject(null); }
        }

        [Test]
        public void ASessionTakesOnlyAllowedScriptsAndAJoinerMustAllowThemToo()
        {
            WriteMod("s", Id, "1.0", "", ("scripts/main.lua", "print('ran')"));
            WriteMod("p", "plain_mod");
            ScanAndEnable(Id, "plain_mod");
            string list = Installed(Id).NetSignature + ";" + Installed("plain_mod").NetSignature;
            CollectionAssert.AreEqual(new[] { "Test " + Id }, GoF2Remake.Multiplayer.NetMods.ScriptMods(list));
            CollectionAssert.AreEqual(new[] { "Test " + Id }, GoF2Remake.Multiplayer.NetMods.UnallowedScripts(list));
            StringAssert.Contains("Test " + Id, GoF2Remake.Multiplayer.NetMods.ScriptRefusal(list));
            ModScripts.SetApproved(Installed(Id), true);
            Assert.IsNull(GoF2Remake.Multiplayer.NetMods.ScriptRefusal(list));
            Assert.IsNull(GoF2Remake.Multiplayer.NetMods.ScriptRefusal(""));
        }

        [Test]
        public void TheExampleScriptModRuns()
        {
            CopyExample("pirate_bounty");
            ScanAndEnable("pirate_bounty");
            var mod = Installed("pirate_bounty");
            Assert.IsFalse(mod.Broken, AllMessages(mod));
            CollectionAssert.AreEqual(new[] { "scripts/main.lua" }, ModScripts.StartFiles(mod));
            ModScripts.SetApproved(mod, true);
            ModScripts.Sync(true);
            Assert.AreEqual("", Errors());
            StringAssert.Contains("Pirate Bounty is running: 250", Out());
            ModScripts.Raise("station", null);
            ModScripts.Raise("shipDestroyed", null, false);
            Assert.IsTrue(ModScripts.RunCommand("bounty", "", "", out string answer), "its own command");
            StringAssert.Contains("the next one pays 250", answer);
            Assert.AreEqual("", Errors());
        }

        [Test]
        public void DescribeListsWhatAClassOffersScripts()
        {
            Run(("scripts/main.lua", "print(game.describe(Session)) print(#game.classes('NpcShip') > 0, #game.classes('System.IO')) print(json.parse('{\\'a\\': [1, 2]}').a[2], json.serialize({ b = 1 }))"));
            Assert.AreEqual("", Errors());
            var lines = ModScripts.Lines.Select(l => l.text).ToList();
            StringAssert.StartsWith("GoF2Remake.Data.Session", lines[0]);
            StringAssert.Contains("static Credits: long", lines[0]);
            Assert.AreEqual("true\t0", lines[1]);
            StringAssert.StartsWith("2\t", lines[2]);
            CollectionAssert.Contains(ScriptAccess.Describe(typeof(ItemData)), "maxPrice: int");
            Assert.IsEmpty(ScriptAccess.Describe(typeof(System.IO.File)));
            Assert.IsTrue(ModScripts.ApiHelp.Contains("game.describe"));
        }

        [Test]
        public void ASyntaxErrorNamesItsLine()
        {
            Assert.IsNull(ModScripts.SyntaxError("local x = 1\nprint(x)", "main.lua"));
            string error = ModScripts.SyntaxError("local x = 1\nprint(x", "main.lua");
            Assert.IsNotNull(error);
            StringAssert.Contains("main.lua", error);
            StringAssert.Contains("2", error);
            var mod = Run(("scripts/main.lua", "print('fine')"), ("scripts/broken.lua", "this is not lua"));
            StringAssert.Contains("broken.lua", Errors());
            Assert.AreEqual("fine", Out(), "the other files still run");
            Assert.IsFalse(mod.Broken);
        }
    }
}
