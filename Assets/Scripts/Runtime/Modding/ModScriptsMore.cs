// ModScriptsMore.cs
// Part of ModScripts: the rest of the "game" table (2026-11-14, "make the scripting as complete as possible"), closing
// what the first version couldn't do.
//   Sequences     game.wait(seconds) / game.waitUntil(fn) pause the function they are called in (an event handler, a
//                 timer, game.run(fn)): it goes on later from that line (a Routine, resumed by Tick). Not inside a hook
//                 or a command, whose caller needs the answer at once.
//   Any member    game.field / game.setField / game.call reach members that aren't public, game.generic generic
//                 methods (ScriptAccess: ModScriptsReflection.cs).
//   The mod's files   game.files / game.readText / game.readJson / game.texture / game.sound: the mod's own files only
//                 (its ModSource), so a script brings its own data, images and sounds.
//   Content keys  game.item / game.ship / game.system / game.station: a mod's content by key -> its number in this game.
//   Components    game.behaviour(object, table): a LuaBehaviour on a GameObject calling the table's functions
//                 (update, lateUpdate, fixedUpdate, enabled, disabled, destroyed, triggerEnter ...).
//   Interface     game.uiEvent(element, "ClickEvent", fn): UI Toolkit's RegisterCallback<T>.
//   Commands      game.addCommand(name, fn): a command of the mod's own, run by "/name" in the chat, by an event
//                 graph's Command node and by game.command (RunCommand, from NetCommands).
//   Players       game.send(event, ...) -> every player's scripts (game.onMessage(event, fn(sender, ...))), through the
//                 host (GameHooks.ScriptSend; single player: this game). game.myId().
//   Settings      game.config: a table per mod kept outside the saves (PlayerPrefs "mod_script_config_<id>").
//   The scene     game.space() / game.station() / game.player(): the level and the player's ship, nil when not there.

using System;
using System.Collections.Generic;
using System.Globalization;
using System.Linq;
using System.Text;
using GoF2Remake.Data;
using MoonSharp.Interpreter;
using UnityEngine;

namespace GoF2Remake.Modding
{
    public static partial class ModScripts
    {
        public const int MaxMessageLength = 900;
        const string ConfigKey = "mod_script_config_";

        /// <summary>The tests' game.config store (never the player's PlayerPrefs); used while ApprovedOverride is set.</summary>
        internal static readonly Dictionary<string, string> ConfigOverride = new Dictionary<string, string>();

        /// <summary>A command of a script mod (game.addCommand): run with its arguments and who asked (a name, "" = the game
        /// itself); false = no script has that command. From NetCommands (the chat, an event graph's Command node).</summary>
        public static bool RunCommand(string name, string args, string who, out string answer)
        {
            answer = null;
            if (vms.Count == 0 || string.IsNullOrEmpty(name)) return false;
            name = name.ToLowerInvariant();
            for (int i = 0; i < vms.Count; i++)
                if (vms[i].RunCommand(name, args ?? "", who ?? "", out answer)) return true;
            return false;
        }

        /// <summary>The names of the script mods' commands (the chat's /help).</summary>
        public static IEnumerable<string> CommandNames => vms.SelectMany(v => v.CommandNames).Distinct();

        /// <summary>A game.send message arrived (from the host's relay, or this game's own in single player): every running
        /// script's game.onMessage handlers of that name get the sender's id and the values.</summary>
        public static void Receive(ulong sender, string name, string payload)
        {
            if (vms.Count == 0 || string.IsNullOrEmpty(name)) return;
            var values = Unpack(payload);
            if (values == null) return;
            var args = new object[values.Count + 1];
            args[0] = (double)sender;
            for (int i = 0; i < values.Count; i++) args[i + 1] = values[i];
            Raise("net:" + name, args);
        }

        /// <summary>An object of a script goes (a component, a texture): at once outside Play mode (the tests).</summary>
        static void Remove(UnityEngine.Object o)
        {
            if (Application.isPlaying) UnityEngine.Object.Destroy(o); else UnityEngine.Object.DestroyImmediate(o);
        }

        /// <summary>After every frame's Update (ModScriptDriver.LateUpdate): "lateUpdate".</summary>
        public static void LateTick(float dt)
        {
            if (vms.Count == 0) return;
            for (int i = 0; i < vms.Count; i++) vms[i].Raise("lateUpdate", new object[] { (double)dt });
        }

        // ---- a message's values as text: numbers, texts, booleans and nil, one a line, each with its kind ----

        static string Pack(IList<DynValue> values, int from)
        {
            var sb = new StringBuilder();
            for (int i = from; i < values.Count; i++)
            {
                var v = values[i];
                if (i > from) sb.Append('\n');
                if (v.Type == DataType.Number) sb.Append('n').Append(v.Number.ToString("R", CultureInfo.InvariantCulture));
                else if (v.Type == DataType.Boolean) sb.Append(v.Boolean ? "t" : "f");
                else if (v.Type == DataType.String) sb.Append('s').Append(v.String.Replace("\\", "\\\\").Replace("\n", "\\n"));
                else sb.Append('-');
            }
            return sb.ToString();
        }

        static List<object> Unpack(string payload)
        {
            var l = new List<object>();
            if (string.IsNullOrEmpty(payload)) return l;
            if (payload.Length > MaxMessageLength * 2) return null;
            foreach (string line in payload.Split('\n'))
            {
                if (line.Length == 0) { l.Add(null); continue; }
                string rest = line.Substring(1);
                switch (line[0])
                {
                    case 'n': l.Add(double.TryParse(rest, NumberStyles.Float, CultureInfo.InvariantCulture, out double d) ? d : 0.0); break;
                    case 't': l.Add(true); break;
                    case 'f': l.Add(false); break;
                    case 's':
                        var sb = new StringBuilder();
                        for (int i = 0; i < rest.Length; i++)
                            if (rest[i] == '\\' && i + 1 < rest.Length) { sb.Append(rest[i + 1] == 'n' ? '\n' : rest[i + 1]); i++; }
                            else sb.Append(rest[i]);
                        l.Add(sb.ToString());
                        break;
                    default: l.Add(null); break;
                }
            }
            return l;
        }

        /// <summary>A function that waits (game.wait / game.waitUntil): resumed by Tick when its time has passed or its
        /// condition holds.</summary>
        sealed class Routine { public MoonSharp.Interpreter.Coroutine co; public float wait; public DynValue until; public int id; public bool removed; }

        sealed partial class Vm
        {
            readonly List<Routine> routines = new List<Routine>();
            readonly Dictionary<string, DynValue> commands = new Dictionary<string, DynValue>(StringComparer.Ordinal);
            readonly Dictionary<string, Texture2D> textures = new Dictionary<string, Texture2D>(StringComparer.OrdinalIgnoreCase);
            readonly List<LuaBehaviour> behaviours = new List<LuaBehaviour>();
            Table config;
            int lastRoutine;

            public IEnumerable<string> CommandNames => commands.Keys;

            // ---- sequences ----

            /// <summary>A function called by the game stopped at a game.wait: it is kept and goes on later.</summary>
            void Adopt(MoonSharp.Interpreter.Coroutine co, DynValue yielded)
            {
                var r = new Routine { co = co, id = nextId++ };
                Park(r, yielded);
                routines.Add(r);
                lastRoutine = r.id;
            }

            static void Park(Routine r, DynValue yielded)
            {
                var y = yielded == null ? DynValue.Nil : yielded.Type == DataType.Tuple ? (yielded.Tuple.Length > 0 ? yielded.Tuple[0] : DynValue.Nil) : yielded;
                r.until = y.Type == DataType.Function || y.Type == DataType.ClrFunction ? y : null;
                r.wait = y.Type == DataType.Number ? (float)y.Number : 0f;
            }

            void Step(Routine r)
            {
                try
                {
                    r.co.AutoYieldCounter = CallBudget;
                    var yielded = r.co.Resume();
                    if (r.co.State == CoroutineState.ForceSuspended) { Error("a call ran for too long and was stopped (an endless loop?)"); r.removed = true; }
                    else if (r.co.State == CoroutineState.Dead) r.removed = true;
                    else Park(r, yielded);
                }
                catch (InterpreterException e) { Error(e.DecoratedMessage ?? e.Message); r.removed = true; }
                catch (Exception e) { Error($"{e.GetType().Name}: {e.Message}"); r.removed = true; }
            }

            void TickRoutines(float dt)
            {
                if (routines.Count == 0) return;
                int n = routines.Count;
                for (int i = 0; i < n && i < routines.Count && !stopped; i++)
                {
                    var r = routines[i];
                    if (r.removed) continue;
                    if (r.until != null)
                    {
                        var holds = Call(r.until, out bool ok, false);
                        if (!ok) { r.removed = true; continue; }
                        if (holds.CastToBool()) Step(r);
                    }
                    else if ((r.wait -= dt) <= 0f) Step(r);
                }
                routines.RemoveAll(r => r.removed);
            }

            // ---- commands ----

            public bool RunCommand(string name, string args, string who, out string answer)
            {
                answer = null;
                if (stopped || !commands.TryGetValue(name, out var fn)) return false;
                var result = Call(fn, out bool ok, false, DynValue.NewString(args), DynValue.NewString(who));
                answer = !ok ? "The command failed (see the mod's warnings)." : result.IsNil() || result.IsVoid() ? "" : result.ToPrintString();
                return true;
            }

            // ---- stopping ----

            void StopMore()
            {
                SaveConfig();
                foreach (var b in behaviours) if (b != null) { b.Detach(); Remove(b); }
                behaviours.Clear();
                foreach (var t in textures.Values) if (t != null) Remove(t);
                textures.Clear();
                routines.Clear();
                commands.Clear();
            }

            void SaveConfig()
            {
                if (config == null) return;
                try
                {
                    string json = MoonSharp.Interpreter.Serialization.Json.JsonTableConverter.TableToJson(config);
                    bool empty = string.IsNullOrEmpty(json) || json == "{}" || json == "[]";
                    if (ApprovedOverride != null) { if (empty) ConfigOverride.Remove(mod.Id); else ConfigOverride[mod.Id] = json; return; }
                    string key = ConfigKey + mod.Id;
                    if (empty) { if (PlayerPrefs.HasKey(key)) PlayerPrefs.DeleteKey(key); }
                    else if (PlayerPrefs.GetString(key, "") != json) { PlayerPrefs.SetString(key, json); PlayerPrefs.Save(); }
                }
                catch (Exception e) { Error("game.config can't be written: " + e.Message); }
            }

            void LoadConfig()
            {
                config = new Table(script);
                string json = ApprovedOverride != null ? (ConfigOverride.TryGetValue(mod.Id, out string o) ? o : "") : PlayerPrefs.GetString(ConfigKey + mod.Id, "");
                if (string.IsNullOrEmpty(json)) return;
                try
                {
                    object parsed = MoonSharp.Interpreter.Serialization.Json.JsonTableConverter.ParseString(json, script, false);
                    var t = parsed as Table ?? (parsed as DynValue)?.Table;
                    if (t != null) foreach (var pair in t.Pairs) config.Set(pair.Key, pair.Value);
                }
                catch (Exception e) { Error("game.config can't be read: " + e.Message); }
            }

            // ---- the mod's own files ----

            /// <summary>A file of this mod by its path in the mod ("data/prices.json"), its own spelling; null = none.</summary>
            string ModFile(string path)
            {
                path = ModSource.Normalise(path);
                if (path.Length == 0 || path.Split('/').Any(p => p == ".." || p == ".")) return null;
                foreach (var f in mod.Source.Files) if (string.Equals(f, path, StringComparison.OrdinalIgnoreCase)) return f;
                return null;
            }

            string ReadModText(string path, string fn)
            {
                string file = ModFile(path);
                if (file == null) return null;
                try { return mod.Source.ReadText(file); }
                catch (Exception e) when (ModFileException.IsFileError(e)) { throw new ScriptRuntimeException($"{fn}: {e.Message}"); }
            }

            // ---- targets of the reflection functions ----

            /// <summary>The object and class a script names: an object, a class (its statics) or a class's name.</summary>
            void Target(CallbackArguments a, string fn, out object target, out Type type)
            {
                target = null; type = null;
                var v = a.Count > 0 ? a[0] : DynValue.Nil;
                if (v.Type == DataType.UserData) { target = v.UserData.Object; type = target?.GetType() ?? v.UserData.Descriptor.Type; }
                else if (v.Type == DataType.String)
                {
                    type = ScriptAccess.Find(v.String, out string error);
                    if (type == null) throw new ScriptRuntimeException(error);
                }
                if (type == null) throw new ScriptRuntimeException($"{fn}: a game object, a class or its name is needed first");
            }

            DynValue Results(object[] results)
            {
                if (results.Length == 1) return ToLua(results[0]);
                return DynValue.NewTuple(results.Select(ToLua).ToArray());
            }

            static Type TypeArgument(DynValue v)
            {
                if (v.Type == DataType.UserData && v.UserData.Object == null) return v.UserData.Descriptor.Type;
                string name = v.Type == DataType.String ? v.String : "";
                switch (name)
                {
                    case "int": return typeof(int);
                    case "float": return typeof(float);
                    case "double": case "number": return typeof(double);
                    case "bool": return typeof(bool);
                    case "string": return typeof(string);
                    case "long": return typeof(long);
                }
                return ScriptAccess.Find(name, out string error) ?? throw new ScriptRuntimeException(error);
            }

            Component SceneObject(string className)
            {
                var t = ScriptAccess.Find(className, out _);
                return t != null && typeof(Component).IsAssignableFrom(t) ? UnityEngine.Object.FindAnyObjectByType(t) as Component : null;
            }

            void BuildMoreApi(Table g)
            {
                // ---- sequences ----
                Fn(g, "wait", a => DynValue.NewYieldReq(new[] { DynValue.NewNumber(a.Count > 0 && a[0].Type == DataType.Number ? Math.Max(0.0, a[0].Number) : 0.0) }));
                Fn(g, "waitUntil", a => DynValue.NewYieldReq(new[] { a.AsType(0, "game.waitUntil", DataType.Function) }));
                Fn(g, "run", a =>
                {
                    var fn = a.AsType(0, "game.run", DataType.Function);
                    lastRoutine = 0;
                    Call(fn, out _, true, a.GetArray(1));
                    return lastRoutine != 0 ? DynValue.NewNumber(lastRoutine) : DynValue.Nil;
                });

                // ---- any member ----
                Fn(g, "field", a =>
                {
                    Target(a, "game.field", out object target, out Type type);
                    return ToLua(ScriptAccess.Field(target, type, a.AsType(1, "game.field", DataType.String).String));
                });
                Fn(g, "setField", a =>
                {
                    Target(a, "game.setField", out object target, out Type type);
                    if (a.Count < 3) throw new ScriptRuntimeException("game.setField(target, name, value)");
                    ScriptAccess.SetField(target, type, a.AsType(1, "game.setField", DataType.String).String, a[2]);
                    return DynValue.Nil;
                });
                Fn(g, "call", a =>
                {
                    Target(a, "game.call", out object target, out Type type);
                    return Results(ScriptAccess.Invoke(target, type, a.AsType(1, "game.call", DataType.String).String, a.GetArray(2), null));
                });
                Fn(g, "generic", a =>
                {
                    Target(a, "game.generic", out object target, out Type type);
                    string name = a.AsType(1, "game.generic", DataType.String).String;
                    var list = a.AsType(2, "game.generic", DataType.Table).Table;
                    var types = list.Values.Select(TypeArgument).ToArray();
                    return Results(ScriptAccess.Invoke(target, type, name, a.GetArray(3), types));
                });
                Fn(g, "patch", Patch);   // any method replaced (ModScriptsPatch.cs)

                // ---- the mod's own files ----
                Fn(g, "files", a =>
                {
                    string folder = a.Count > 0 && a[0].Type == DataType.String ? ModSource.Normalise(a[0].String).TrimEnd('/') : "";
                    var table = new Table(script);
                    foreach (var f in mod.Source.Files)
                        if (folder.Length == 0 || f.StartsWith(folder + "/", StringComparison.OrdinalIgnoreCase)) table.Append(DynValue.NewString(f));
                    return DynValue.NewTable(table);
                });
                Fn(g, "readText", a =>
                {
                    string text = ReadModText(a.AsType(0, "game.readText", DataType.String).String, "game.readText");
                    return text != null ? DynValue.NewString(text) : DynValue.Nil;
                });
                Fn(g, "readJson", a =>
                {
                    string path = a.AsType(0, "game.readJson", DataType.String).String;
                    string text = ReadModText(path, "game.readJson");
                    if (text == null) return DynValue.Nil;
                    try
                    {
                        object parsed = MoonSharp.Interpreter.Serialization.Json.JsonTableConverter.ParseString(text, script, false);
                        return parsed as DynValue ?? (parsed is Table t ? DynValue.NewTable(t) : DynValue.Nil);
                    }
                    catch (Exception e) { throw new ScriptRuntimeException($"game.readJson: {path}: {e.Message}"); }
                });
                Fn(g, "texture", a =>
                {
                    string path = a.AsType(0, "game.texture", DataType.String).String;
                    string file = ModFile(path);
                    if (file == null) return DynValue.Nil;
                    if (textures.TryGetValue(file, out var cached) && cached != null) return ToLua(cached);
                    byte[] bytes;
                    try { bytes = mod.Source.ReadBytes(file); }
                    catch (Exception e) when (ModFileException.IsFileError(e)) { throw new ScriptRuntimeException("game.texture: " + e.Message); }
                    var tex = bytes != null ? ImageImport.Decode(bytes, file, false, true, out string error) : null;
                    if (tex == null) throw new ScriptRuntimeException($"game.texture: {file} is no image the game reads");
                    tex.name = mod.Id + ":" + file;
                    textures[file] = tex;
                    return ToLua(tex);
                });
                Fn(g, "sound", a =>
                {
                    string path = a.AsType(0, "game.sound", DataType.String).String;
                    var done = a.AsType(1, "game.sound", DataType.Function);
                    string file = ModFile(path);
                    if (file == null) { Call(done, DynValue.Nil); return DynValue.False; }
                    ModAudio.Load(mod, file, clip => { if (!stopped) Call(done, ToLua(clip)); });
                    return DynValue.True;
                });

                // ---- content keys ----
                DynValue Number(bool found, int index) => found ? DynValue.NewNumber(index) : DynValue.Nil;
                string Key(CallbackArguments a, string fn)
                {
                    var v = a.Count > 0 ? a[0] : DynValue.Nil;
                    string s = v.Type == DataType.Number ? ((int)v.Number).ToString(CultureInfo.InvariantCulture) : a.AsType(0, fn, DataType.String).String.Trim();
                    return s;
                }
                // (a bare id is this mod's own content first: "pulse_cannon" = "<this mod>:pulse_cannon")
                Fn(g, "item", a => { string k = Key(a, "game.item"); bool f = (k.IndexOf(':') < 0 && ModContent.TryResolveItem(mod.Id + ":" + k, out int i)) || ModContent.TryResolveItem(k, out i); return Number(f, i); });
                Fn(g, "ship", a => { string k = Key(a, "game.ship"); bool f = (k.IndexOf(':') < 0 && ModContent.TryResolveShip(mod.Id + ":" + k, out int i)) || ModContent.TryResolveShip(k, out i); return Number(f, i); });
                Fn(g, "system", a => { string k = Key(a, "game.system"); bool f = (k.IndexOf(':') < 0 && ModWorld.TryResolveSystem(mod.Id + ":" + k, out int i)) || ModWorld.TryResolveSystem(k, out i); return Number(f, i); });
                Fn(g, "station", a =>
                {
                    if (a.Count == 0) return ToLua(SceneObject("GoF2Remake.World.StationLevel"));   // game.station(): the docked level
                    string k = Key(a, "game.station");
                    bool f = (k.IndexOf(':') < 0 && ModWorld.TryResolveStation(mod.Id + ":" + k, out int i)) || ModWorld.TryResolveStation(k, out i);
                    return Number(f, i);
                });

                // ---- the scene ----
                Fn(g, "space", a => ToLua(SceneObject("GoF2Remake.World.SpaceLevel")));
                Fn(g, "player", a =>
                {
                    var level = SceneObject("GoF2Remake.World.SpaceLevel");
                    return level != null ? ToLua(ScriptAccess.Field(level, level.GetType(), "Player")) : DynValue.Nil;
                });
                Fn(g, "myId", a => DynValue.NewNumber(NetSession.LocalId));

                // ---- components written in Lua ----
                Fn(g, "behaviour", a =>
                {
                    var owner = Owner(a, "game.behaviour");
                    var table = a.AsType(1, "game.behaviour", DataType.Table).Table;
                    if (owner == null) throw new ScriptRuntimeException("game.behaviour: the object is gone");
                    var b = owner.AddComponent<LuaBehaviour>();
                    table["gameObject"] = ToLua(owner);
                    table["transform"] = ToLua(owner.transform);
                    table["component"] = ToLua(b);
                    var self = DynValue.NewTable(table);
                    b.Attach((message, args) =>
                    {
                        if (stopped) return;
                        var fn = table.Get(message);
                        if (fn.Type != DataType.Function) return;
                        var lua = new DynValue[(args?.Length ?? 0) + 1];
                        lua[0] = self;
                        for (int i = 1; i < lua.Length; i++) lua[i] = ToLua(args[i - 1]);
                        Call(fn, out bool ok, lua);
                        if (!ok && message != "destroyed" && ++b.Failures >= MaxFailures)
                        {
                            Error($"a behaviour's \"{message}\" failed {MaxFailures} times in a row: the behaviour was removed");
                            b.Detach();
                            Remove(b);
                        }
                    });
                    behaviours.Add(b);
                    behaviours.RemoveAll(x => x == null);
                    return self;
                });

                // ---- UI Toolkit events ----
                Fn(g, "uiEvent", a =>
                {
                    object element = a.Count > 0 && a[0].Type == DataType.UserData ? a[0].UserData.Object : null;
                    if (!(element is UnityEngine.UIElements.CallbackEventHandler handler)) throw new ScriptRuntimeException("game.uiEvent: an interface element is needed first");
                    var eventType = TypeArgument(a.Count > 1 ? a[1] : DynValue.Nil);
                    if (!typeof(UnityEngine.UIElements.EventBase).IsAssignableFrom(eventType)) throw new ScriptRuntimeException($"game.uiEvent: {eventType.Name} is no interface event (ClickEvent, PointerDownEvent ...)");
                    var fn = a.AsType(2, "game.uiEvent", DataType.Function);
                    var callbackType = typeof(UnityEngine.UIElements.EventCallback<>).MakeGenericType(eventType);
                    var callback = ScriptAccess.MakeDelegate(callbackType, fn);
                    System.Reflection.MethodInfo Find(string name) => typeof(UnityEngine.UIElements.CallbackEventHandler).GetMethods()
                        .First(m => m.Name == name && m.IsGenericMethodDefinition && m.GetGenericArguments().Length == 1 && m.GetParameters().Length == 2
                                    && m.GetParameters()[1].ParameterType == typeof(UnityEngine.UIElements.TrickleDown)).MakeGenericMethod(eventType);
                    var trickle = UnityEngine.UIElements.TrickleDown.NoTrickleDown;
                    Find("RegisterCallback").Invoke(handler, new object[] { callback, trickle });
                    var unregister = Find("UnregisterCallback");
                    Action off = () => unregister.Invoke(handler, new object[] { callback, trickle });
                    int id = nextId++;
                    undo.Add(off);
                    undoById[id] = off;
                    return DynValue.NewNumber(id);
                });

                // ---- commands of the mod's own ----
                Fn(g, "addCommand", a =>
                {
                    string name = a.AsType(0, "game.addCommand", DataType.String).String.Trim().TrimStart('/').ToLowerInvariant();
                    if (name.Length == 0 || name.Any(c => !(c >= 'a' && c <= 'z' || c >= '0' && c <= '9' || c == '_')))
                        throw new ScriptRuntimeException("game.addCommand: a command's name is letters, digits and _");
                    commands[name] = a.AsType(1, "game.addCommand", DataType.Function);
                    return DynValue.Nil;
                });

                // ---- messages between the players' scripts ----
                Fn(g, "send", a =>
                {
                    string name = a.AsType(0, "game.send", DataType.String).String;
                    string payload = Pack(a.GetArray(), 1);
                    if (name.Length == 0 || name.Length > 64 || name.IndexOf('\n') >= 0) throw new ScriptRuntimeException("game.send: a message's name is 1 to 64 characters");
                    if (payload.Length > MaxMessageLength) throw new ScriptRuntimeException($"game.send: the values are too long ({payload.Length} of {MaxMessageLength} characters)");
                    bool sent = GameHooks.ScriptSend != null ? GameHooks.ScriptSend(name, payload) : Local(name, payload);
                    return DynValue.NewBoolean(sent);
                });
                Fn(g, "onMessage", a =>
                {
                    string name = "net:" + a.AsType(0, "game.onMessage", DataType.String).String;
                    if (!handlers.TryGetValue(name, out var list)) handlers[name] = list = new List<Handler>();
                    var h = new Handler { fn = a.AsType(1, "game.onMessage", DataType.Function), id = nextId++ };
                    list.Add(h);
                    return DynValue.NewNumber(h.id);
                });

                // ---- settings of the mod's own ----
                LoadConfig();
                g["config"] = config;
                Fn(g, "saveConfig", a => { SaveConfig(); return DynValue.Nil; });
            }

            static bool Local(string name, string payload) { Receive(0, name, payload); return true; }
        }
    }
}
