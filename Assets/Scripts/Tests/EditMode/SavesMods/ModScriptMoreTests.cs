// ModScriptMoreTests.cs
// Feature: docs/notes/mods.md "Mod scripts" > what was added to make scripting complete (ModScriptsMore.cs,
// ModScriptsReflection.cs, LuaBehaviour.cs): functions that wait (game.wait / waitUntil / run), members that aren't
// public and generic methods (game.field / setField / call / generic), Lua functions as any delegate of the game's
// (assigned, passed, with out values), the mod's own files (readText / readJson / files / texture), content keys,
// components written in Lua, UI events, the mod's own commands, messages between scripts, game.config.

using System.Collections.Generic;
using System.Linq;
using System.Reflection;
using GoF2Remake.Data;
using GoF2Remake.Modding;
using NUnit.Framework;
using UnityEngine;

namespace GoF2Remake.Tests
{
    public class ModScriptMoreTests : SavesModsTest
    {
        const string Id = "script_mod";

        [SetUp]
        public void OwnApprovals()
        {
            ModScripts.StopAll();
            ModScripts.ClearLines();
            ModScripts.ApprovedOverride = new HashSet<string>(System.StringComparer.OrdinalIgnoreCase);
            ModScripts.ConfigOverride.Clear();
            GoF2Remake.Flight.SpaceHooksInstaller.Install();
            GoF2Remake.Multiplayer.NetHooksInstaller.Install();
        }

        [TearDown]
        public void StopScripts()
        {
            ModScripts.StopAll();
            ModScripts.ClearLines();
            ModScripts.ApprovedOverride = null;
            ModScripts.ConfigOverride.Clear();
        }

        ModInfo Run(params (string path, string text)[] files)
        {
            WriteMod("s", Id, "1.0", "", files);
            return Start();
        }

        ModInfo Start()
        {
            ScanAndEnable(Id);
            ModScripts.SetApproved(Installed(Id), true);
            ModScripts.Sync(true);
            return Installed(Id);
        }

        static string Out() => string.Join("|", ModScripts.Lines.Where(l => !l.error).Select(l => l.text));
        static string Errors() => string.Join("|", ModScripts.Lines.Where(l => l.error).Select(l => l.text));

        [Test]
        public void AFunctionWaitsAndGoesOnLater()
        {
            Run(("scripts/main.lua", "game.on('go', function(n) print('a', n) game.wait(1) print('b') game.waitUntil(function() return ready end) print('c') end) game.on('ready', function() ready = true end) local h = game.run(function() game.wait(1) print('never') end) print(h ~= nil, game.off(h))"));
            Assert.AreEqual("true\ttrue", Out());
            ModScripts.Raise("go", 7);
            ModScripts.Tick(0.5f);
            Assert.AreEqual("true\ttrue|a\t7", Out());
            ModScripts.Tick(0.6f);
            Assert.AreEqual("true\ttrue|a\t7|b", Out());
            ModScripts.Tick(5f);
            Assert.AreEqual("true\ttrue|a\t7|b", Out(), "the condition doesn't hold yet");
            ModScripts.Raise("ready");
            ModScripts.Tick(0f);
            Assert.AreEqual("", Errors());
            Assert.AreEqual("true\ttrue|a\t7|b|c", Out());
        }

        [Test]
        public void WaitingInsideAHookIsAnError()
        {
            var was = GameHooks.Notice;
            try
            {
                GameHooks.Notice = text => true;
                Run(("scripts/main.lua", "game.hook('GameHooks', 'Notice', function(original, text) game.wait(1) return true end)"));
                Assert.IsTrue(GameHooks.Notice("x"), "the game's own delegate answers instead");
                StringAssert.Contains("game.wait can't be used here", Errors());
            }
            finally { ModScripts.StopAll(); GameHooks.Notice = was; }
        }

        [Test]
        public void MembersThatArentPublicAreReached()
        {
            Session.Credits = 500;
            Run(("scripts/main.lua", "print(game.field('Session', 'credits')) game.setField(Session, 'credits', 750) local r = ModManager.Revision game.call('ModManager', 'Invalidate') print(ModManager.Revision > r, game.field('ModManager', 'sessionIds'))"),
                ("scripts/b.lua", "local mods = import('ModManager').Installed local m = game.get(mods, 0) print(pcall(game.call, m, 'LocalFile', 'mod.json'))"),
                ("scripts/c.lua", "print(pcall(game.field, 'GamePaths', 'Override')) print(pcall(game.field, Session, 'nothing'))"));
            Assert.AreEqual("", Errors());
            Assert.AreEqual(750, Session.Credits);
            var lines = ModScripts.Lines.Select(l => l.text).ToList();
            Assert.AreEqual("500", lines[0]);
            Assert.AreEqual("true\tnil", lines[1]);
            StringAssert.StartsWith("false", lines[2], "a method that takes a file name stays out of reach");
            StringAssert.StartsWith("false", lines[3], "and so does a class that is left out");
            StringAssert.StartsWith("false", lines[4]);
        }

        [Test]
        public void ClassesAreGlobalsByTheirBareNames()
        {
            Run(("scripts/main.lua", "print(Standing.Pirate, Standing == import('GoF2Remake.Flight.Standing'), NoSuchClassAnywhere, GamePaths, File, rawget(_G, 'Standing') ~= nil)"));
            Assert.AreEqual("", Errors());
            Assert.AreEqual("8\ttrue\tnil\tnil\tnil\ttrue", Out(), "a class once used is a real global; what scripts can't use stays nil");
        }

        [Test]
        public void GenericMethodsTakeTheirTypesByName()
        {
            Run(("scripts/main.lua", "local go = GameObject.__new('lua generic test') local c = game.generic(go, 'AddComponent', { 'UnityEngine.BoxCollider' }) print(c ~= nil, game.generic(go, 'GetComponent', { 'BoxCollider' }) == c, game.component(go, 'BoxCollider') == c) import('UnityEngine.Object').DestroyImmediate(go)"));
            Assert.AreEqual("", Errors());
            Assert.AreEqual("true\ttrue\ttrue", Out());
        }

        [Test]
        public void ALuaFunctionStandsInForADelegateOfTheGame()
        {
            var was = GameHooks.Notice;
            var wasCondition = GameHooks.Condition;
            try
            {
                GameHooks.Condition = (string expression, out string error) => { error = "E:" + expression; return false; };
                Run(("scripts/main.lua", "GameHooks.Notice = function(text) print('N', text) return text == 'yes' end game.hook('GameHooks', 'Condition', function(original, expression) if expression == 'lua' then return true, nil end return original(expression) end)"));
                Assert.AreEqual("", Errors());
                Assert.IsTrue(GameHooks.Notice("yes"));
                Assert.IsFalse(GameHooks.Notice("no"));
                Assert.AreEqual("N\tyes|N\tno", Out());
                Assert.IsTrue(GameHooks.Condition("lua", out string e));
                Assert.IsNull(e);
                Assert.IsFalse(GameHooks.Condition("other", out e), "the game's own answer, passed through the script");
                Assert.AreEqual("E:other", e, "with its out value");
                Assert.AreEqual("", Errors());
            }
            finally { ModScripts.StopAll(); GameHooks.Notice = was; GameHooks.Condition = wasCondition; }
        }

        [Test]
        public void AScriptReadsItsOwnModsFiles()
        {
            string dir = WriteMod("s", Id, "1.0", "", ("data/note.txt", "hello"), ("data/prices.json", "{ 'ore': 12, 'list': [1, 2, 3] }"),
                ("scripts/main.lua", "local p = game.readJson('data/prices.json') print(game.readText('data/note.txt'), p.ore, #p.list, #game.files('data'), game.readText('../mod.json'), game.readText('nope.txt')) local t = game.texture('icons/dot.png') print(t.width, t.height, game.texture('icons/dot.png') == t, game.texture('nope.png'))"));
            var png = new Texture2D(4, 2);
            WriteFile(dir, "icons/dot.png", png.EncodeToPNG());
            Object.DestroyImmediate(png);
            Start();
            Assert.AreEqual("", Errors());
            Assert.AreEqual("hello\t12\t3\t2\tnil\tnil|4\t2\ttrue\tnil", Out());
        }

        [Test]
        public void ContentKeysGiveTheNumbersOfThisGame()
        {
            Run(("scripts/main.lua", "print(game.item('5'), game.item(5), game.ship('nope:x'), game.system('0'), game.station('1'), game.station(), game.space(), game.player(), game.myId())"));
            Assert.AreEqual("", Errors());
            Assert.AreEqual("5\t5\tnil\t0\t1\tnil\tnil\tnil\t0", Out());
        }

        [Test]
        public void AModsOwnCommandRunsFromTheChatAGraphAndAScript()
        {
            Run(("scripts/main.lua", "game.addCommand('bonus', function(args, who) return 'bonus ' .. args .. ' for ' .. who end) print(game.command('bonus 3'), game.command('nonesuch 1'))"));
            Assert.AreEqual("", Errors());
            StringAssert.StartsWith("bonus 3 for \t", Out());
            Assert.IsTrue(ModScripts.RunCommand("BONUS", "5", "Keith", out string answer));
            Assert.AreEqual("bonus 5 for Keith", answer);
            Assert.AreEqual("bonus 7 for ", GoF2Remake.Multiplayer.NetCommands.RunOnServer("bonus", "7", null), "an event graph's Command node");
            Assert.IsFalse(ModScripts.RunCommand("other", "", "", out _));
            CollectionAssert.AreEqual(new[] { "bonus" }, ModScripts.CommandNames.ToArray());
        }

        [Test]
        public void MessagesReachTheScriptsWithTheirSender()
        {
            Run(("scripts/main.lua", "game.onMessage('hi', function(sender, a, b, c, d) print(sender, a, b, c, d) end) print(game.send('hi', 1.5, 'two\\nlines \\\\ here', true, nil)) print(pcall(game.send, 'hi', string.rep('x', 2000)))"));
            Assert.AreEqual("", Errors());
            var lines = ModScripts.Lines.Select(l => l.text).ToList();
            Assert.AreEqual("0\t1.5\ttwo\nlines \\ here\ttrue\tnil", lines[0]);
            Assert.AreEqual("true", lines[1]);
            StringAssert.StartsWith("false", lines[2], "too long a message is refused");
            ModScripts.Receive(3, "hi", "n42\nsxyz");
            Assert.AreEqual("3\t42\txyz\tnil\tnil", ModScripts.Lines.Last().text, "one that came from another player");
        }

        [Test]
        public void ConfigOutlivesTheScripts()
        {
            Run(("scripts/main.lua", "game.config.runs = (game.config.runs or 0) + 1 print(game.config.runs)"));
            ModScripts.Sync(true);
            ModScripts.Sync(true);
            Assert.AreEqual("", Errors());
            Assert.AreEqual("1|2|3", Out());
        }

        [Test]
        public void ATableBecomesAComponent()
        {
            Run(("scripts/main.lua", "go = GameObject.__new('lua behaviour test') game.behaviour(go, { n = 0, update = function(self, dt) self.n = self.n + 1 print('u', self.n, self.gameObject.name) end, destroyed = function(self) print('gone') end })"));
            Assert.AreEqual("", Errors());
            var b = Object.FindAnyObjectByType<LuaBehaviour>();
            Assert.IsNotNull(b);
            try
            {
                var update = typeof(LuaBehaviour).GetMethod("Update", BindingFlags.NonPublic | BindingFlags.Instance);
                update.Invoke(b, null);
                update.Invoke(b, null);
                Assert.AreEqual("u\t1\tlua behaviour test|u\t2\tlua behaviour test", Out());
                ModScripts.StopAll();
                Assert.IsTrue(b == null, "stopping the scripts takes their components off");
            }
            finally { Object.DestroyImmediate(GameObject.Find("lua behaviour test")); }
        }

        [Test]
        public void InterfaceEventsAndLateUpdate()
        {
            Run(("scripts/main.lua", "local e = import('UnityEngine.UIElements.VisualElement').__new() local h = game.uiEvent(e, 'ClickEvent', function(evt) print('clicked') end) print(game.off(h), pcall(game.uiEvent, e, 'Vector3', print)) game.on('lateUpdate', function(dt) print('late', dt) end)"));
            ModScripts.LateTick(0.5f);
            Assert.AreEqual("", Errors());
            var lines = ModScripts.Lines.Select(l => l.text).ToList();
            StringAssert.StartsWith("true\tfalse", lines[0]);
            Assert.AreEqual("late\t0.5", lines[1]);
        }
    }
}
