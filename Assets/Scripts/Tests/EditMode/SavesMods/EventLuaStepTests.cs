// EventLuaStepTests.cs
// Feature: docs/notes/event-graphs.md "Lua step" (the user's request 2026-11-21: "a lua node for the graph so it can also run
// code"): the event graphs' Lua node (EventGraphScript "EventLuaNode": Code, Result) and the Mod Builder missions' "lua"
// step (QuestSteps) both write the line "lua <variable | -> <the code as base64>"; the runner runs it in the scripts of the
// mod the event came from (EventRun.RunLua -> ModScripts.RunCode): the chunk's argument is the table event (name,
// get(expression), set(variable, number)), a number or true / false it returns goes into the variable. It is never a
// server command (nobody types it in the chat), an event outside a mod can't run code, a mod with a Lua step counts as a
// script mod (the player's approval, whose hash covers the events/ files that name "lua"), and without the approval the
// step fails with a message.

using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using GoF2Remake.Data;
using GoF2Remake.Events;
using GoF2Remake.Modding;
using GoF2Remake.Multiplayer;
using NUnit.Framework;
using UnityEngine;
using UnityEngine.TestTools;

namespace GoF2Remake.Tests
{
    public class EventLuaStepTests : SavesModsTest
    {
        const string Id = "lua_step_mod";

        [SetUp]
        public void OwnApprovals()
        {
            ModScripts.StopAll();
            ModScripts.ClearLines();
            ModScripts.ApprovedOverride = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
            EventRunner.Reset();
        }

        [TearDown]
        public void StopScripts()
        {
            EventRunner.Reset();
            ModScripts.StopAll();
            ModScripts.ClearLines();
            ModScripts.ApprovedOverride = null;
        }

        static string Errors() => string.Join("|", ModScripts.Lines.Where(l => l.error).Select(l => l.text));
        static string Out() => string.Join("|", ModScripts.Lines.Where(l => !l.error).Select(l => l.text));
        static string Decode(string line) => Encoding.UTF8.GetString(Convert.FromBase64String(line.Substring(line.LastIndexOf(' ') + 1)));

        static string Quest(string code, string result = null) =>
            "{ \"format\": \"gof2mission\", \"kind\": \"event\", \"steps\": [ { \"type\": \"lua\", \"code\": " + Newtonsoft.Json.JsonConvert.ToString(code)
            + (result != null ? ", \"result\": \"" + result + "\"" : "") + " }, { \"type\": \"command\", \"line\": \"set done = 1\" } ] }";

        void Mod(bool approve, params (string path, string text)[] files)
        {
            WriteMod("s", Id, "1.0", "", files);
            ScanAndEnable(Id);
            if (approve) ModScripts.SetApproved(Installed(Id), true);
            ModScripts.Sync(true);
        }

        [Test]
        public void TheGraphsLuaNodeWritesItsCodeAsOneWord()
        {
            const string code = "local event = ...\nif event.get('kills') > { 1, 2 }[1] then return true end  -- a # too";
            var g = EventsTestUtil.WithStart();
            EventsTestUtil.Node(g, "n", "EventLuaNode", ("Code", code), ("Result", "Was Busy"));
            EventsTestUtil.Wire(g, "start", "Next", "n", "In");
            var problems = new List<EventGraphScript.Problem>();
            var lines = EventsTestUtil.Lines(EventGraphScript.Compile(g, problems));
            Assert.IsEmpty(EventsTestUtil.Errors(problems), EventsTestUtil.Describe(problems));
            string line = lines.Single(l => l.StartsWith("lua "));
            StringAssert.StartsWith("lua was_busy ", line);
            Assert.AreEqual(code, Decode(line), "line breaks, braces and # survive");

            var empty = EventsTestUtil.WithStart();
            EventsTestUtil.Node(empty, "n", "EventLuaNode", ("Code", "  "), ("Result", ""));
            EventsTestUtil.Wire(empty, "start", "Next", "n", "In");
            problems.Clear();
            Assert.IsFalse(EventsTestUtil.Lines(EventGraphScript.Compile(empty, problems)).Any(l => l.StartsWith("lua")));
            Assert.IsTrue(problems.Any(p => p.warning && p.message.Contains("empty Lua node")));
        }

        [Test]
        public void TheMissionsLuaStepWritesTheSameLine()
        {
            var issues = new List<string>();
            var lines = EventsTestUtil.Lines(QuestFile.Compile(Quest("return 2 + 3", "five"), issues));
            Assert.IsEmpty(issues, string.Join("; ", issues));
            string line = lines.Single(l => l.StartsWith("lua "));
            StringAssert.StartsWith("lua five ", line);
            Assert.AreEqual("return 2 + 3", Decode(line));
            Assert.IsNull(EventRunner.CheckScript(string.Join("\n", lines)), "the runner reads it");
            Assert.IsNotNull(QuestSteps.Types.FirstOrDefault(t => t.type == "lua"), "the Mod Builder offers the step");
        }

        [Test]
        public void TheCodeRunsInTheModsScripts()
        {
            Mod(true, ("scripts/main.lua", "count = 10 function bump(n) count = count + n return count end"));
            var set = new Dictionary<string, double>();
            Assert.IsTrue(ModScripts.RunCode(Id, "local event = ...\nevent.set('seen', event.get('2 + 3'))\nevent.set('flag', true)\nprint(event.name)\nreturn bump(5)", "trial",
                                             e => e == "2 + 3" ? 5 : 0, (k, v) => set[k] = v, out double? result, out string error), error);
            Assert.AreEqual(15, result, "the mod's own globals and functions");
            Assert.AreEqual(5, set["seen"]);
            Assert.AreEqual(1, set["flag"]);
            Assert.AreEqual("trial", Out());
            Assert.IsTrue(ModScripts.RunCode(Id, "return count > 100", "trial", e => 0, (k, v) => { }, out result, out _));
            Assert.AreEqual(0, result, "false is 0");
            Assert.IsTrue(ModScripts.RunCode(Id, "count = 0", "trial", e => 0, (k, v) => { }, out result, out _));
            Assert.IsNull(result, "nothing returned");

            Assert.IsFalse(ModScripts.RunCode(Id, "this is not lua", "trial", e => 0, (k, v) => { }, out _, out error));
            StringAssert.Contains("Lua step", error);
            Assert.IsFalse(ModScripts.RunCode(Id, "error('boom')", "trial", e => 0, (k, v) => { }, out _, out error));
            StringAssert.Contains("boom", Errors());
            Assert.IsFalse(ModScripts.RunCode("no_such_mod", "return 1", "trial", e => 0, (k, v) => { }, out _, out error));
            StringAssert.Contains("to be allowed", error);
        }

        [Test]
        public void AModWithALuaStepNeedsThePlayersYes()
        {
            Mod(false, ("events/trial.gof2mission", Quest("return 1")));
            var mod = Installed(Id);
            Assert.IsEmpty(ModScripts.Files(mod), "no script file");
            CollectionAssert.AreEqual(new[] { "events/trial.gof2mission" }, ModScripts.LuaEventFiles(mod));
            Assert.IsTrue(ModScripts.HasScripts(mod), "its Lua step makes it a script mod");
            Assert.IsFalse(ModScripts.IsApproved(mod));
            Assert.IsFalse(ModScripts.RunCode(Id, "return 1", "trial", e => 0, (k, v) => { }, out _, out string error), "not allowed yet");
            string hash = ModScripts.ScriptHash(mod);

            ModScripts.SetApproved(mod, true);
            ModScripts.Sync(true);
            Assert.IsTrue(ModScripts.RunCode(Id, "return 1", "trial", e => 0, (k, v) => { }, out double? result, out error), error);
            Assert.AreEqual(1, result);

            // Other code in the step: another hash, so the player is asked again.
            WriteMod("s", Id, "1.0", "", ("events/trial.gof2mission", Quest("return 2")));
            ModManager.Scan();
            Assert.AreNotEqual(hash, ModScripts.ScriptHash(Installed(Id)));
            Assert.IsFalse(ModScripts.IsApproved(Installed(Id)));

            // A mod whose events have no Lua stays an ordinary mod.
            WriteMod("p", "plain_events", "1.0", "", ("events/quiet.gof2mission", "{ \"format\": \"gof2mission\", \"kind\": \"event\", \"steps\": [ { \"type\": \"command\", \"line\": \"set a = 1\" } ] }"));
            ModManager.Scan();
            Assert.IsFalse(ModScripts.HasScripts(Installed("plain_events")));
        }

        [Test]
        public void AnEventRunsItsLuaStepAndKeepsTheResult()
        {
            // Edit mode: the single-player runner and the event screen make DontDestroyOnLoad objects, which only Play mode
            // allows; stand-ins in their static slots keep them from being made.
            var stubs = new List<GameObject>();
            void Stub<T>() where T : Component
            {
                var go = new GameObject(typeof(T).Name + " (test)");
                stubs.Add(go);
                typeof(T).GetField("instance", System.Reflection.BindingFlags.NonPublic | System.Reflection.BindingFlags.Static).SetValue(null, go.AddComponent<T>());
            }
            Stub<LocalEvents>();
            Stub<EventScreen>();
            try
            {
                Mod(true, ("scripts/main.lua", "ran = 0"),
                          ("events/trial.gof2mission", Quest("local event = ...\nran = ran + 1\nSession.Credits = 4242\nevent.set([[from_lua]], 7)\nreturn event.get([[3 * 3]])", "nine")));
                Session.Credits = 0;
                string started = EventRunner.Command("trial", null);
                Assert.IsTrue(EventRunner.Running, started);
                for (int i = 0; i < 5 && Session.Credits == 0; i++) EventRunner.Tick();
                Assert.AreEqual(4242, Session.Credits, "the step ran: " + Errors());
                Assert.AreEqual("", Errors());
                Assert.IsTrue(EventRunner.Number("0", out _, out _));
                Assert.AreEqual("lua", NetCommands.RunOnServer("lua", "- " + Convert.ToBase64String(Encoding.UTF8.GetBytes("Session.Credits = 1")), null) == null ? "lua" : "ran",
                                "\"lua\" is no server command");
                Assert.AreEqual(4242, Session.Credits, "nothing typed in the chat runs code");
            }
            finally
            {
                EventRunner.Reset();
                typeof(LocalEvents).GetField("instance", System.Reflection.BindingFlags.NonPublic | System.Reflection.BindingFlags.Static).SetValue(null, null);
                typeof(EventScreen).GetField("instance", System.Reflection.BindingFlags.NonPublic | System.Reflection.BindingFlags.Static).SetValue(null, null);
                foreach (var go in stubs) UnityEngine.Object.DestroyImmediate(go);
            }
        }
    }
}
