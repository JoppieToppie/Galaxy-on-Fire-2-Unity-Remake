// ModBuilderMcpTests.cs
// Feature: CLAUDE.md "Mods" > Mod Builder MCP server, and the quest format for AIs (Events.QuestSchema, written to
// Modding/ai/gof2-modding/quest.schema.json / quests.md by GoF2 > Build > Modding AI Reference). The schema has a step
// definition for every row of QuestSteps, with every key a new step of that type has; the markdown names every step type.
// The server's JSON-RPC (ModBuilderMcp.Dispatch, without the HTTP layer or the main-thread pump): initialize echoes a known
// protocol version and gives a session, ping answers, tools/list lists every tool with its annotations (the ones that change
// the project aren't read-only, removal is destructive), an unknown tool or method is a JSON-RPC error, a notification gets
// no answer. ModelViewer: every glyph of its font 3 x 5, a model drawn with its hardpoint markers, an unknown angle refused.

using System.Linq;
using System.Reflection;
using GoF2Remake.Events;
using GoF2Remake.UI;
using Newtonsoft.Json.Linq;
using NUnit.Framework;

namespace GoF2Remake.Tests
{
    public class ModBuilderMcpTests
    {
        static JObject StepSchema(JObject schema, string type) =>
            ((JArray)schema["$defs"]["step"]["oneOf"]).OfType<JObject>().FirstOrDefault(s => (string)s["properties"]?["type"]?["const"] == type);

        [Test]
        public void SchemaHasEveryStepTypeAndItsKeys()
        {
            var schema = QuestSchema.Build();
            Assert.AreEqual("gof2mission", (string)schema["properties"]["format"]["const"]);
            foreach (var t in QuestSteps.Types)
            {
                var def = StepSchema(schema, t.type);
                Assert.IsNotNull(def, $"no schema for step '{t.type}'");
                var props = (JObject)def["properties"];
                foreach (var key in QuestSteps.New(t.type).Properties().Select(p => p.Name))
                    Assert.IsNotNull(props[key], $"step '{t.type}': its key '{key}' isn't in the schema");
            }
        }

        [Test]
        public void MarkdownNamesEveryStepType()
        {
            string md = QuestSchema.Markdown();
            foreach (var t in QuestSteps.Types)
                StringAssert.Contains("`" + t.type + "`", md, $"quests.md misses '{t.type}'");
        }

        static JObject Dispatch(JObject message, out string session)
        {
            var m = typeof(ModBuilderMcp).GetMethod("Dispatch", BindingFlags.NonPublic | BindingFlags.Static);
            var args = new object[] { message, null };
            var answer = (JObject)m.Invoke(null, args);
            session = (string)args[1];
            return answer;
        }

        static JObject Rpc(string method, JObject parameters = null, int? id = 1) =>
            new JObject { ["jsonrpc"] = "2.0", ["method"] = method, ["params"] = parameters ?? new JObject(), ["id"] = id.HasValue ? (JToken)id.Value : null };

        [Test]
        public void InitializeEchoesAKnownVersionAndGivesASession()
        {
            var a = Dispatch(Rpc("initialize", new JObject { ["protocolVersion"] = "2025-03-26", ["clientInfo"] = new JObject { ["name"] = "test", ["version"] = "1" } }), out string session);
            Assert.AreEqual("2025-03-26", (string)a["result"]["protocolVersion"]);
            Assert.IsNotNull(a["result"]["capabilities"]["tools"]);
            Assert.IsFalse(string.IsNullOrEmpty((string)a["result"]["instructions"]));
            Assert.IsFalse(string.IsNullOrEmpty(session));

            var b = Dispatch(Rpc("initialize", new JObject { ["protocolVersion"] = "1999-01-01" }), out _);
            Assert.AreEqual("2025-06-18", (string)b["result"]["protocolVersion"], "an unknown version gets the newest");
        }

        [Test]
        public void TheOldQuestNamesStillWork()
        {
            var m = typeof(ModBuilderMcp).GetMethod("FromOldNames", BindingFlags.NonPublic | BindingFlags.Static);
            var args = new JObject { ["kind"] = "quests", ["quest"] = new JObject { ["title"] = "T" } };
            Assert.AreEqual("compile_mission", m.Invoke(null, new object[] { "compile_quest", args }));
            Assert.AreEqual("missions", (string)args["kind"]);
            Assert.IsNotNull(args["mission"]);
            Assert.IsNull(args["quest"]);
            Assert.AreEqual("get_mission_schema", m.Invoke(null, new object[] { "get_quest_schema", new JObject() }));
            var play = new JObject { ["mode"] = "quest" };
            Assert.AreEqual("play_test", m.Invoke(null, new object[] { "play_test", play }));
            Assert.AreEqual("mission", (string)play["mode"]);
        }

        [Test]
        public void ToolsListHasEveryToolWithItsAnnotations()
        {
            var a = Dispatch(Rpc("tools/list"), out _);
            var tools = ((JArray)a["result"]["tools"]).OfType<JObject>().ToDictionary(t => (string)t["name"]);
            foreach (var name in new[] { "builder_status", "search_content", "list_entries", "get_entries", "put_entry", "delete_entry",
                                         "list_files", "read_file", "write_file", "compile_mission", "validate", "get_mission_schema",
                                         "play_test", "end_play_test", "get_log", "screenshot", "undo", "redo", "show_kind",
                                         "view_model", "view_texture", "list_textures", "select_entry", "view", "hardpoint", "engines",
                                         "test_fly", "end_test_fly", "weapon_range", "switch_hangar", "list_projects", "create_project",
                                         "open_project", "export_mod", "patch_entry", "loadout", "room_marker", "press", "steer", "ui_buttons", "ui_click", "read_guide" })
            {
                Assert.IsTrue(tools.ContainsKey(name), $"missing tool {name}");
                Assert.AreEqual("object", (string)tools[name]["inputSchema"]["type"], name);
            }
            foreach (var name in new[] { "put_entry", "patch_entry", "delete_entry", "write_file", "undo", "redo", "play_test" })
                Assert.IsFalse((bool)tools[name]["annotations"]["readOnlyHint"], $"{name} changes the project");
            foreach (var name in new[] { "builder_status", "search_content", "get_entries", "read_file", "validate", "get_log", "view_model", "view_texture", "list_textures", "list_projects", "ui_buttons", "read_guide" })
                Assert.IsTrue((bool)tools[name]["annotations"]["readOnlyHint"], $"{name} only reads");
            Assert.IsTrue((bool)tools["delete_entry"]["annotations"]["destructiveHint"]);
        }

        [Test]
        public void KeyNamesAndGameActionsResolve()
        {
            Assert.AreEqual("Enter", McpInput.KeyName("enter"));
            Assert.AreEqual("Digit1", McpInput.KeyName("1"));
            Assert.AreEqual("Escape", McpInput.KeyName("esc"));
            Assert.AreEqual("UpArrow", McpInput.KeyName("upArrow"));
            Assert.IsNull(McpInput.KeyName("nosuchkey"));
            // every game action the press tool documents exists among the bindings
            foreach (var id in new[] { "action", "boost", "brake", "firePrimary", "fireSecondary", "autopilotMenu", "actionsMenu", "levelOut", "fastForward", "camera", "cloak", "switchSecondary" })
                Assert.IsTrue(GoF2Remake.Flight.GameControls.Rows.Any(r => r.id == id), id);
            StringAssert.Contains("No action", McpInput.HoldAction("nosuchaction", 0.1f));
            StringAssert.Contains("steer", McpInput.HoldAction("steer", 0.1f));
            McpInput.Uninstall();
        }

        [Test]
        public void EveryGlyphIsThreeByFive()
        {
            var glyphs = (System.Collections.Generic.Dictionary<char, string>)typeof(ModelViewer).GetField("Glyphs", BindingFlags.NonPublic | BindingFlags.Static).GetValue(null);
            foreach (var kv in glyphs) Assert.AreEqual(15, kv.Value.Length, $"glyph '{kv.Key}'");
            foreach (var a in ModelViewer.Angles)
                foreach (char ch in a.name.ToUpperInvariant()) Assert.IsTrue(glyphs.ContainsKey(ch), $"no glyph for '{ch}' (angle {a.name})");
            foreach (var a in ModelViewer.DefaultAngles) Assert.IsTrue(ModelViewer.Angles.Any(x => x.name == a), a);
        }

        [Test]
        public void ModelViewerDrawsAModelWithItsMarkers()
        {
            var cube = UnityEngine.GameObject.CreatePrimitive(UnityEngine.PrimitiveType.Cube);
            cube.transform.localScale = new UnityEngine.Vector3(10f, 4f, 20f);
            try
            {
                var o = new ModelViewer.Options
                {
                    angles = new[] { "left", "top" }, tile = 128,
                    markers = new System.Collections.Generic.List<ModelViewer.Marker> { new ModelViewer.Marker { local = new UnityEngine.Vector3(0f, 0.5f, 0.5f), type = 0, label = "P1" } },
                };
                var tex = ModelViewer.Render(cube, o, false, out string info, out string error);
                Assert.IsNotNull(tex, error);
                Assert.AreEqual(256, tex.width);
                Assert.AreEqual(128, tex.height);
                StringAssert.Contains("2 view(s)", info);
                var px = tex.GetPixels32();
                var bg = (UnityEngine.Color32)new UnityEngine.Color(0.32f, 0.33f, 0.36f);
                Assert.Greater(px.Count(p => System.Math.Abs(p.r - bg.r) > 12 || System.Math.Abs(p.g - bg.g) > 12), 500, "the model is drawn");
                var red = (UnityEngine.Color32)GoF2Remake.World.ModBuilderGizmo.TypeColours[0];
                Assert.Greater(px.Count(p => p.r == red.r && p.g == red.g && p.b == red.b), 10, "the primary marker is drawn");
                UnityEngine.Object.DestroyImmediate(tex);
                Assert.IsNull(ModelViewer.Render(cube, new ModelViewer.Options { angles = new[] { "sideways" } }, false, out _, out error));
                StringAssert.Contains("Unknown angle", error);
            }
            finally { UnityEngine.Object.DestroyImmediate(cube); }
        }

        [Test]
        public void PingUnknownMethodsAndNotifications()
        {
            Assert.IsNotNull(Dispatch(Rpc("ping"), out _)["result"]);
            Assert.AreEqual(-32601, (int)Dispatch(Rpc("no/such"), out _)["error"]["code"]);
            Assert.AreEqual(-32602, (int)Dispatch(Rpc("tools/call", new JObject { ["name"] = "no_such_tool" }), out _)["error"]["code"]);
            var note = new JObject { ["jsonrpc"] = "2.0", ["method"] = "notifications/initialized" };
            Assert.IsNull(Dispatch(note, out _), "a notification has no answer");
        }
    }
}
