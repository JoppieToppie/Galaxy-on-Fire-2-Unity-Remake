// ModScriptsApi.cs
// Part of ModScripts: what a mod's Lua state sees of the game, the global table "game" (Modding/README.md "Scripts"
// lists it for modders; keep the two in step).
//   game.mod { id, name, version }, game.version, game.saved (a table kept in the save file, per mod)
//   game.on(event, fn) -> handle        the game's events (ModScripts.Raise) and other scripts' game.emit
//   game.after(seconds, fn) / game.every(seconds, fn) -> handle      game time: they wait while the game is paused
//   game.off(handle)                    an event handler, a timer or a hook
//   game.emit(event, ...)               an event for every running script (numbers, texts and booleans travel)
//   game.log(...) / print(...)          the builder's console and the log
//   game.message(text)                  on screen (the flight HUD's message line, a toast in a station)
//   game.command(line) -> answer        a chat / event command as the server runs it ("spawn pirate 3")
//   game.type(name) / import(name)      a game or Unity class: its statics, name.__new(...) makes one
//   game.hook(target, member, fn)       a delegate field / property replaced: fn(original, ...) decides the result
//   game.listen(target, member, fn)     a C# event or delegate added to: fn(...)
//   game.find(class) / game.findOne(class)        the scene's components of a class
//   game.component(object, class) / game.components(object, class) / game.addComponent(object, class)
//   game.table(list) -> a Lua copy;  game.get(collection, key) / game.set(collection, key, value)
//   game.describe(class or object) -> its members as text;  game.classes(part of a name) -> the class names
//   game.multiplayer() -> "none" | "host" | "client";  game.scene() -> the scene's name
// The Unity and game classes scripts use most are globals already (Globals below).

using System;
using System.Collections;
using System.Collections.Generic;
using System.Linq;
using System.Reflection;
using GoF2Remake.Data;
using MoonSharp.Interpreter;
using UnityEngine;

namespace GoF2Remake.Modding
{
    public static partial class ModScripts
    {
        /// <summary>Classes every script has as a global of their short name.</summary>
        static readonly string[] Globals =
        {
            "UnityEngine.Vector2", "UnityEngine.Vector3", "UnityEngine.Quaternion", "UnityEngine.Color", "UnityEngine.Mathf",
            "UnityEngine.Time", "UnityEngine.GameObject", "UnityEngine.Debug", "UnityEngine.Resources",
            "GoF2Remake.Data.Session", "GoF2Remake.Data.Database", "GoF2Remake.Data.Settings", "GoF2Remake.Data.Localization",
        };

        /// <summary>The API in short, for the Mod Builder's script editor (Modding/README.md "Scripts" has it in full).</summary>
        public const string ApiHelp =
@"FILES
  scripts/main.lua        runs first when the mod starts
  scripts/other.lua       every .lua directly in scripts/ runs too (by name)
  scripts/lib/util.lua    a module: local util = require(""lib.util"")
                          a dependency's: require(""othermod:lib.util"")

THE GAME TABLE
  game.mod.id / .name / .version      this mod
  game.saved                          a table kept in the player's save file
                                      (numbers, texts, booleans, tables)
  game.config                         a table kept outside the saves (the
                                      mod's own settings); game.saveConfig()
  game.on(event, fn) -> handle        listen to an event (list below)
  game.after(seconds, fn) -> handle   once, in game time
  game.every(seconds, fn) -> handle   again and again
  game.off(handle)                    stop a handler, timer, hook or sequence
  game.emit(event, ...)               an event for every script mod (this game)
  game.log(...) or print(...)         to this console and the game's log
  game.message(text)                  a line on the player's screen
  game.tutorial(id, title, text, repeatable) -> queued
                                      native tutorial popup; once per save by default
                                      respects the Tutorial Hints setting
  game.command(line) -> answer        a chat / event command, as the server:
                                      game.command(""title @a Welcome for 5"")
  game.addCommand(name, fn)           a command of your own: fn(args, who)
                                      returns its answer; ""/name"" in the chat,
                                      an event graph's Command node, game.command
  game.multiplayer()                  ""none"", ""host"" or ""client""
  game.scene()                        ""MainMenu"", ""Space"", ""Station""
  game.space() / game.station()       the flight level / the docked level, or nil
  game.player()                       the player's ship in flight, or nil

SEQUENCES  (in any handler, timer or game.run; not in a hook or command)
  game.wait(seconds)                  this function goes on later, from here
  game.waitUntil(function() return Session.Credits > 5000 end)
  game.run(function() ... end) -> handle     starts a function that may wait
      game.on(""space"", function()
          game.wait(3)
          game.command(""spawn @a Phantom pirate 2"")
          game.waitUntil(function() return #game.find(""NpcShip"") == 0 end)
          game.message(""All clear"")
      end)

EVENTS  (game.on)
  update(dt), lateUpdate(dt) every frame; dt = 0 while paused
  scene(name)                a scene was loaded
  space(level)               the flight level stands (launch, jump, load)
  station(level)             docked: the hangar stands
  database(db)               the game's tables were loaded: change them here
  shipDestroyed(ship, byPlayer)
  playerDestroyed()
  piracyKillRegistered(killerId, victimId, station, count, threshold, windowSeconds)
  piracyStatusChanged(pilotId, isPirate)  server only, requires the piracy mod option
  trade(item, amount, price, station)   bought (+) or sold (-) in a shop
  missionAccepted(mission), missionSucceeded(mission, pay), missionFailed(mission)
  storyStep(step, previous)  the campaign moved on
  chat(from, text, own)      a chat line arrived (multiplayer)
  gameLoaded()               a save was loaded (game.saved is that save's)
  saving()                   just before game.saved is written
  stop()                     this mod's scripts are stopping

THE GAME'S CLASSES
  Every class of the game and of Unity's scene namespaces is a global by its
  name: Session, Database, Settings, Standing, GameHooks, Vector3, Mathf ...
  import(""Full.Name"") where two classes share a name.
  Class.__new(...)                    makes one: Vector3.__new(0, 1, 0)
  Session.Credits = Session.Credits + 1000
  print(game.describe(Session))       what a class or object offers scripts
  print(game.describe(Session, true)) ... its private members too
  game.classes(""Ship"")                the class names with that in them
  game.find(""NpcShip"")                the scene's components of a class
  game.findOne(""SpaceLevel"")
  game.component(object, ""Class"") / game.components / game.addComponent
  game.table(list)                    a Lua copy of a C# list / dictionary
  game.get(collection, key) / game.set(collection, key, value)
                                      lists count from 0
  Members that aren't public, and generic methods:
  game.field(target, ""name"") / game.setField(target, ""name"", value)
  game.call(target, ""Method"", ...)    returns its result, then its out values
  game.generic(target, ""Method"", { ""Class"" }, ...)
  A Lua function works wherever the game takes a delegate:
      SpaceHooks.WreckHacked = function(station, wreck) ... end

HOOKS
  game.hook(target, ""Member"", function(original, ...) ... end)
      replaces a delegate of the game; call original(...) to run the game's
      own code, return what the game should get (after it, the values of the
      delegate's out parameters):
      game.hook(""GameHooks"", ""FactionBuyPrice"", function(original, station, price)
          return math.floor(original(station, price) / 2)
      end)
  Server piracy rules (requires the piracy mod option):
      game.hook(""GameHooks"", ""PiracyKillThreshold"", function() return 40 end)
      game.hook(""GameHooks"", ""PiracyWindowSeconds"", function() return 5 * 86400 end)
  game.listen(target, ""Member"", function(...) ... end)
      adds to a C# event or delegate: game.listen(""Settings"", ""Changed"", fn)
  The hook classes GameHooks, SpaceHooks, UiHooks and NetSession hold most
  of what the game's layers tell each other.

PATCHES
  game.patch(target, ""Method"", function(original, self, ...) ... end)
      replaces any method of a game class, public or not (no self for a
      static method). original(self, ...) runs the code it replaced; return
      the result (then the method's ref / out values):
      game.patch(""Shop"", ""GenerateItems"", function(original, db, station)
          local list = original(db, station)
          list:RemoveAll(function(s) return db:Item(s.item).techLevel > 5 end)
          return list
      end)
  Overloads: name the parameter types as a 4th argument, ""Database, int"".
  game.describe(X, true) lists a class's methods. Not patchable: constructors,
  generic methods, lambdas, a struct's own methods, file / network methods.

YOUR MOD'S FILES AND CONTENT
  game.files(""data"")                  the mod's files (in a folder)
  game.readText(""data/note.txt"")      a text file of the mod, or nil
  game.readJson(""data/prices.json"")   ... as a table
  game.texture(""icons/badge.png"")     ... as a Texture2D
  game.sound(""sounds/ping.ogg"", function(clip) ... end)    ... as an AudioClip
  game.item(""my_gun"") / game.ship(""other_mod:falcon"") /
  game.system(...) / game.station(...)   content by its key -> its number here

OBJECTS AND INTERFACE
  game.behaviour(gameObject, {        a component written in Lua
      update = function(self, dt) ... end,     -- also lateUpdate, fixedUpdate,
      destroyed = function(self) ... end })    -- enabled, disabled,
                                               -- triggerEnter, triggerExit,
                                               -- collisionEnter
      self.gameObject, self.transform
  game.uiEvent(element, ""ClickEvent"", function(event) ... end) -> handle
  game.style(element, { position = ""absolute"", top = 40, left = ""5%"",
                        color = ""#ff8800"", fontSize = 18, display = ""none"" })
      an interface element's own styles (UI Toolkit's names; nil takes one off)

OTHER PLAYERS  (multiplayer; in single player the message comes back to you)
  game.send(""name"", ...)              to every player's scripts (numbers,
                                      texts, booleans; through the host)
  game.onMessage(""name"", function(sender, ...) ... end)    sender: 0 = the host
  game.myId()                         this player's id
  Never trust another player's values: check them on the host.

LIMITS
  Lua 5.2 (MoonSharp). No io, no os but the clock, no .NET classes, no files
  but the mod's own: scripts reach the game and Unity's scene classes only.
  Structs are copies: set transform.position as a whole, not its .x.
  A call that runs too long is stopped; errors show here and in the mod
  browser, never crash the game. Players are asked before a mod with
  scripts runs.";

        sealed class Tutorial
        {
            public string modId, id, title, text;
            public int scene;
            public bool repeatable;
            public Action shown;
        }

        static readonly List<Tutorial> tutorials = new List<Tutorial>();

        static void ShowTutorials()
        {
            if (tutorials.Count == 0) return;
            if (!Settings.TutorialHints) { tutorials.Clear(); return; }
            int scene = UnityEngine.SceneManagement.SceneManager.GetActiveScene().handle;
            tutorials.RemoveAll(t => t.scene != scene);
            if (tutorials.Count == 0 || GameHooks.TutorialPopup == null) return;
            var t = tutorials[0];
            try
            {
                if (!GameHooks.TutorialPopup(t.title, t.text)) return;
                tutorials.RemoveAt(0);
                t.shown?.Invoke();
            }
            catch (Exception e)
            {
                tutorials.Remove(t);
                Debug.LogWarning("Mod tutorial " + t.modId + ":" + t.id + ": " + e.Message);
            }
        }

        sealed class Message { public string text; public float left; }

        /// <summary>game.message's lines waiting for an interface to show them (the oldest first, one a frame).</summary>
        static readonly List<Message> messages = new List<Message>();

        /// <summary>Every frame: the oldest waiting message goes on screen; one nothing can show yet (GameHooks.Notice false,
        /// or the interface threw while it is being built) waits up to three seconds of real time, then is dropped.</summary>
        static void ShowMessages()
        {
            if (messages.Count == 0) return;
            var m = messages[0];
            bool shown;
            try { shown = GameHooks.Notice != null && GameHooks.Notice(m.text); }
            catch (Exception) { shown = false; }
            if (shown || (m.left -= Time.unscaledDeltaTime) <= 0f) messages.RemoveAt(0);
        }

        /// <summary>A delegate made from a Lua function was called by the game (ScriptAccess.MakeDelegate).</summary>
        internal static DynValue CallFromDelegate(DynValue fn, object[] args)
        {
            if (fn?.Function == null || !byScript.TryGetValue(fn.Function.OwnerScript, out var vm) || !ScriptAccess.OnMainThread) return DynValue.Nil;
            var lua = new DynValue[args?.Length ?? 0];
            for (int i = 0; i < lua.Length; i++) lua[i] = vm.ToLua(args[i]);
            return vm.Call(fn, lua);
        }

        sealed partial class Vm
        {
            void BuildApi()
            {
                var g = new Table(script);
                script.Globals["game"] = g;
                var info = new Table(script);
                info["id"] = mod.Id; info["name"] = mod.Name; info["version"] = mod.Version;
                g["mod"] = info;
                g["version"] = Application.version;
                g["saved"] = saved;
                BuildMoreApi(g);   // ModScriptsMore.cs: sequences, any member, the mod's files, components, commands, messages

                Fn(g, "on", a =>
                {
                    string name = a.AsType(0, "game.on", DataType.String).String;
                    var fn = a.AsType(1, "game.on", DataType.Function);
                    if (!handlers.TryGetValue(name, out var list)) handlers[name] = list = new List<Handler>();
                    var h = new Handler { fn = fn, id = nextId++ };
                    list.Add(h);
                    return DynValue.NewNumber(h.id);
                });
                Fn(g, "after", a => AddTimer(a, "game.after", false));
                Fn(g, "every", a => AddTimer(a, "game.every", true));
                Fn(g, "off", a =>
                {
                    if (a.Count == 0 || a[0].Type != DataType.Number) return DynValue.False;
                    int id = (int)a[0].Number;
                    bool found = false;
                    foreach (var list in handlers.Values) foreach (var h in list) if (h.id == id && !h.removed) { h.removed = true; found = true; }
                    foreach (var t in timers) if (t.id == id && !t.removed) { t.removed = true; found = true; }
                    foreach (var r in routines) if (r.id == id && !r.removed) { r.removed = true; found = true; }
                    if (undoById.TryGetValue(id, out var u)) { undoById.Remove(id); undo.Remove(u); u(); found = true; }
                    return DynValue.NewBoolean(found);
                });
                Fn(g, "emit", a =>
                {
                    string name = a.AsType(0, "game.emit", DataType.String).String;
                    var args = new object[Math.Max(0, a.Count - 1)];
                    for (int i = 1; i < a.Count; i++)
                        args[i - 1] = a[i].Type == DataType.Number ? a[i].Number : a[i].Type == DataType.String ? a[i].String
                            : a[i].Type == DataType.Boolean ? a[i].Boolean : (object)null;
                    ModScripts.Raise(name, args);
                    return DynValue.Nil;
                });
                Fn(g, "log", a =>
                {
                    AddLine(mod, string.Join("\t", a.GetArray().Select(v => v.ToPrintString())), false);
                    return DynValue.Nil;
                });
                Fn(g, "message", a =>
                {
                    // Shown from the next frame on (ShowMessages): an event such as "station" comes while the scene's
                    // interface is still being built. The console has it at once.
                    string text = a.AsType(0, "game.message", DataType.String).String;
                    AddLine(mod, "[message] " + text, false);
                    if (messages.Count < 20) messages.Add(new Message { text = text, left = 3f });
                    return DynValue.Nil;
                });
                Fn(g, "tutorial", a =>
                {
                    string id = a.AsType(0, "game.tutorial", DataType.String).String.Trim();
                    string title = a.AsType(1, "game.tutorial", DataType.String).String.Trim();
                    string text = a.AsType(2, "game.tutorial", DataType.String).String.Trim();
                    bool repeatable = a.Count > 3 && a[3].CastToBool();
                    if (id.Length == 0 || id.Length > 128 || title.Length == 0 || title.Length > 120 || text.Length == 0 || text.Length > 2000)
                        throw new ScriptRuntimeException("game.tutorial: id (1-128), title (1-120) and text (1-2000 characters) required");
                    string key = "__tutorial_seen:" + id;
                    if (!Settings.TutorialHints || (!repeatable && saved.Get(key).CastToBool())
                        || tutorials.Count >= 20 || tutorials.Any(t => t.modId == mod.Id && t.id == id))
                        return DynValue.False;
                    tutorials.Add(new Tutorial
                    {
                        modId = mod.Id, id = id, title = title, text = text, repeatable = repeatable,
                        scene = UnityEngine.SceneManagement.SceneManager.GetActiveScene().handle,
                        shown = () => { if (!stopped && !repeatable) saved.Set(key, DynValue.True); }
                    });
                    return DynValue.True;
                });
                Fn(g, "command", a =>
                {
                    string line = a.AsType(0, "game.command", DataType.String).String.Trim().TrimStart('/');
                    string answer = GameHooks.RunCommand != null ? GameHooks.RunCommand(line) : "Commands can't run here.";
                    return answer != null ? DynValue.NewString(answer) : DynValue.Nil;
                });
                Fn(g, "multiplayer", a => DynValue.NewString(!NetSession.Active ? "none" : NetSession.Hosting ? "host" : "client"));
                Fn(g, "scene", a => DynValue.NewString(UnityEngine.SceneManagement.SceneManager.GetActiveScene().name));

                DynValue type = Fn(g, "type", a => Static(a.AsType(0, "game.type", DataType.String).String));
                script.Globals["import"] = type;
                foreach (string full in Globals)
                {
                    var t = ScriptAccess.Find(full, out _);
                    var s = t != null ? UserData.CreateStatic(t) : null;
                    if (s != null) script.Globals[t.Name] = s;
                }

                // What a class offers scripts, as text (the console's own reference), and the classes by a part of their name.
                Fn(g, "describe", a =>
                {
                    Type t = null;
                    string error = null;
                    if (a.Count > 0 && a[0].Type == DataType.UserData) t = a[0].UserData.Object?.GetType() ?? a[0].UserData.Descriptor.Type;
                    else if (a.Count > 0 && a[0].Type == DataType.String) t = ScriptAccess.Find(a[0].String, out error);
                    if (t == null) throw new ScriptRuntimeException(error ?? "game.describe: a game object, a class or its name is needed");
                    var lines = ScriptAccess.Describe(t, a.Count > 1 && a[1].CastToBool());   // (x, true): the private members too
                    return DynValue.NewString(t.FullName.Replace('+', '.') + (lines.Count > 0 ? "\n  " + string.Join("\n  ", lines) : " (nothing scripts can use)"));
                });
                Fn(g, "classes", a =>
                {
                    string part = a.Count > 0 && a[0].Type == DataType.String ? a[0].String : "";
                    var table = new Table(script);
                    foreach (var t in ScriptAccess.AllowedTypes())
                    {
                        string full = t.FullName.Replace('+', '.');
                        if (part.Length > 0 && full.IndexOf(part, StringComparison.OrdinalIgnoreCase) < 0) continue;
                        table.Append(DynValue.NewString(full));
                        if (table.Length >= 300) break;
                    }
                    return DynValue.NewTable(table);
                });

                // Any other class by its bare name: a global that isn't set yet is looked up among the classes scripts can
                // use (a name only one class has, the game's winning over Unity's) and kept ("Standing.Pirate" without an
                // import). A name that is no class stays nil, as an unset global is.
                var meta = new Table(script);
                meta["__index"] = DynValue.NewCallback((context, a) =>
                {
                    if (a.Count < 2 || a[1].Type != DataType.String) return DynValue.Nil;
                    var t = ScriptAccess.Find(a[1].String, out _);
                    var s = t != null ? UserData.CreateStatic(t) : null;
                    if (s == null) return DynValue.Nil;
                    script.Globals[a[1].String] = s;
                    return s;
                }, "class");
                script.Globals.MetaTable = meta;

                Fn(g, "hook", a => Hook(a, "game.hook", true));
                Fn(g, "listen", a => Hook(a, "game.listen", false));

                Fn(g, "find", a =>
                {
                    var t = ComponentType(a, 0, "game.find");
                    var table = new Table(script);
                    foreach (var o in UnityEngine.Object.FindObjectsByType(t, FindObjectsInactive.Exclude)) table.Append(ToLua(o));
                    return DynValue.NewTable(table);
                });
                Fn(g, "findOne", a => ToLua(UnityEngine.Object.FindAnyObjectByType(ComponentType(a, 0, "game.findOne"))));
                Fn(g, "component", a => ToLua(Owner(a, "game.component")?.GetComponent(ComponentType(a, 1, "game.component"))));
                Fn(g, "components", a =>
                {
                    var table = new Table(script);
                    var owner = Owner(a, "game.components");
                    if (owner != null) foreach (var c in owner.GetComponentsInChildren(ComponentType(a, 1, "game.components"), true)) table.Append(ToLua(c));
                    return DynValue.NewTable(table);
                });
                Fn(g, "addComponent", a => ToLua(Owner(a, "game.addComponent")?.AddComponent(ComponentType(a, 1, "game.addComponent"))));

                Fn(g, "table", a =>
                {
                    var table = new Table(script);
                    object o = a.Count > 0 && a[0].Type == DataType.UserData ? a[0].UserData.Object : null;
                    if (a.Count > 0 && a[0].Type == DataType.Table) return a[0];
                    if (o is IDictionary d) foreach (DictionaryEntry e in d) table.Set(ToLua(e.Key), ToLua(e.Value));
                    else if (o is IEnumerable e) foreach (var x in e) table.Append(ToLua(x));
                    else throw new ScriptRuntimeException("game.table: a list, a set or a dictionary of the game's is needed");
                    return DynValue.NewTable(table);
                });
                Fn(g, "get", a =>
                {
                    object o = Collection(a, "game.get");
                    if (o is IDictionary d) { object k = Key(d.GetType(), a[1]); return d.Contains(k) ? ToLua(d[k]) : DynValue.Nil; }
                    var l = (IList)o;
                    int i = (int)a.AsType(1, "game.get", DataType.Number).Number;
                    return i >= 0 && i < l.Count ? ToLua(l[i]) : DynValue.Nil;
                });
                Fn(g, "set", a =>
                {
                    object o = Collection(a, "game.set");
                    if (a.Count < 3) throw new ScriptRuntimeException("game.set(collection, key, value)");
                    if (o is IDictionary d)
                    {
                        object k = Key(d.GetType(), a[1]);
                        if (a[2].IsNil()) d.Remove(k); else d[k] = ScriptAccess.FromLua(a[2], Arg(d.GetType(), 1));
                        return DynValue.Nil;
                    }
                    var l = (IList)o;
                    int i = (int)a.AsType(1, "game.set", DataType.Number).Number;
                    if (i < 0 || i >= l.Count) throw new ScriptRuntimeException($"game.set: index {i} is outside the list (0..{l.Count - 1})");
                    l[i] = ScriptAccess.FromLua(a[2], Arg(l.GetType(), 0));
                    return DynValue.Nil;
                });
            }

            DynValue Fn(Table g, string name, Func<CallbackArguments, DynValue> body)
            {
                var v = DynValue.NewCallback((context, args) => body(args), name);
                g[name] = v;
                return v;
            }

            DynValue AddTimer(CallbackArguments a, string name, bool repeat)
            {
                float seconds = Mathf.Max(0f, (float)a.AsType(0, name, DataType.Number).Number);
                var t = new Timer { fn = a.AsType(1, name, DataType.Function), id = nextId++, left = seconds, every = repeat ? Mathf.Max(seconds, 0.01f) : 0f };
                timers.Add(t);
                return DynValue.NewNumber(t.id);
            }

            /// <summary>A class's statics as a Lua value (registered on first use, if scripts may see it).</summary>
            DynValue Static(string name)
            {
                var t = ScriptAccess.Find(name, out string error);
                if (t == null) throw new ScriptRuntimeException(error);
                return UserData.CreateStatic(t) ?? throw new ScriptRuntimeException($"\"{name}\" can't be used from scripts");
            }

            Type ComponentType(CallbackArguments a, int index, string fn)
            {
                var v = a.Count > index ? a[index] : DynValue.Nil;
                Type t = null;
                string error = null;
                if (v.Type == DataType.String) t = ScriptAccess.Find(v.String, out error);
                else if (v.Type == DataType.UserData && v.UserData.Object == null) t = v.UserData.Descriptor.Type;
                if (t == null) throw new ScriptRuntimeException(error ?? $"{fn}: a class or its name is needed");
                if (!typeof(Component).IsAssignableFrom(t)) throw new ScriptRuntimeException($"{fn}: {t.Name} is not a component");
                return t;
            }

            static GameObject Owner(CallbackArguments a, string fn)
            {
                object o = a.Count > 0 && a[0].Type == DataType.UserData ? a[0].UserData.Object : null;
                if (o is GameObject go) return go;
                if (o is Component c) return c != null ? c.gameObject : null;
                throw new ScriptRuntimeException($"{fn}: a GameObject or a component is needed first");
            }

            static object Collection(CallbackArguments a, string fn)
            {
                object o = a.Count > 1 && a[0].Type == DataType.UserData ? a[0].UserData.Object : null;
                if (o is IDictionary || o is IList) return o;
                throw new ScriptRuntimeException($"{fn}: a list or a dictionary of the game's and a key are needed");
            }

            static Type Arg(Type collection, int index)
            {
                var args = collection.IsGenericType ? collection.GetGenericArguments() : Type.EmptyTypes;
                return index < args.Length ? args[index] : typeof(object);
            }

            static object Key(Type dictionary, DynValue key) => ScriptAccess.FromLua(key, Arg(dictionary, 0));

            // ---- game.hook / game.listen ----

            /// <summary>hook: a public delegate field or property of a game object or class is replaced by the Lua function,
            /// which gets the delegate it replaced as its first argument (to call or not) and decides the result. listen: the
            /// function is added to a C# event, or to a delegate beside what is there (its result unused). Both are undone
            /// when the script stops or by game.off.</summary>
            DynValue Hook(CallbackArguments a, string name, bool wrap)
            {
                object target = null;
                Type type = null;
                if (a.Count > 0 && a[0].Type == DataType.UserData) { target = a[0].UserData.Object; type = target?.GetType() ?? a[0].UserData.Descriptor.Type; }
                else if (a.Count > 0 && a[0].Type == DataType.String)
                {
                    type = ScriptAccess.Find(a[0].String, out string error);
                    if (type == null) throw new ScriptRuntimeException(error);
                }
                if (type == null || !ScriptAccess.Allowed(type)) throw new ScriptRuntimeException($"{name}: a game object or a class (or its name) is needed first");
                string member = a.AsType(1, name, DataType.String).String;
                var fn = a.AsType(2, name, DataType.Function);
                const BindingFlags F = BindingFlags.Public | BindingFlags.Instance | BindingFlags.Static | BindingFlags.FlattenHierarchy;

                Action off;
                var evt = type.GetEvent(member, F);
                if (evt != null)
                {
                    if (wrap) throw new ScriptRuntimeException($"{name}: {type.Name}.{member} is an event: use game.listen");
                    var handler = ScriptAccess.MakeDelegate(evt.EventHandlerType, fn);
                    evt.AddEventHandler(target, handler);
                    off = () => evt.RemoveEventHandler(target, handler);
                }
                else
                {
                    var field = type.GetField(member, F);
                    var prop = field == null ? type.GetProperty(member, F) : null;
                    Type dt = field?.FieldType ?? prop?.PropertyType;
                    if (dt == null || !typeof(Delegate).IsAssignableFrom(dt) || (prop != null && (!prop.CanRead || !prop.CanWrite)))
                        throw new ScriptRuntimeException($"{name}: {type.Name} has no public delegate or event \"{member}\"");
                    if (target == null && !(field?.IsStatic ?? prop.GetMethod.IsStatic)) throw new ScriptRuntimeException($"{name}: {type.Name}.{member} needs an object, not the class");
                    Func<Delegate> get = () => (Delegate)(field != null ? field.GetValue(target) : prop.GetValue(target));
                    Action<Delegate> set = d => { if (field != null) field.SetValue(target, d); else prop.SetValue(target, d); };
                    var original = get();
                    if (wrap)
                    {
                        var ps = dt.GetMethod("Invoke").GetParameters();
                        // What the Lua function calls to run the game's own code: its arguments back as C# values.
                        bool hasOut = ps.Any(p => p.ParameterType.IsByRef);
                        // The game's own delegate with its out / ref values after its result (a delegate with none: just the result).
                        object Original(object[] clr)
                        {
                            object r = original?.DynamicInvoke(clr);
                            if (!hasOut) return r;
                            var values = new List<object> { r };
                            for (int i = 0; i < ps.Length; i++) if (ps[i].ParameterType.IsByRef) values.Add(clr[i]);
                            return new ScriptAccess.Multi { values = values.ToArray() };
                        }
                        var callOriginal = DynValue.NewCallback((context, args) =>
                        {
                            if (original == null) return DynValue.Nil;
                            var clr = new object[ps.Length];
                            for (int i = 0; i < ps.Length; i++) clr[i] = ps[i].IsOut ? null : ScriptAccess.Argument(i < args.Count ? args[i] : DynValue.Nil, ps[i].ParameterType);
                            object r = Original(clr);
                            return r is ScriptAccess.Multi m ? DynValue.NewTuple(m.values.Select(ToLua).ToArray()) : ToLua(r);
                        }, member);
                        bool removed = false;
                        Delegate mine = null;
                        mine = ScriptAccess.MakeDelegate(dt, args =>
                        {
                            // Off, stopped or called from a worker thread: the game's own delegate, untouched.
                            if (removed || stopped || !ScriptAccess.OnMainThread) return Original(args);
                            var lua = new DynValue[args.Length + 1];
                            lua[0] = callOriginal;
                            for (int i = 0; i < args.Length; i++) lua[i + 1] = ToLua(args[i]);
                            var result = Call(fn, out bool ok, false, lua);
                            return ok ? result : Original(args);   // a failing hook leaves the game as it was
                        });
                        set(mine);
                        off = () => { removed = true; if (ReferenceEquals(get(), mine)) set(original); };
                    }
                    else
                    {
                        var handler = ScriptAccess.MakeDelegate(dt, fn);
                        set(Delegate.Combine(handler, original));   // (the game's own last: its result stands)
                        off = () => set(Delegate.Remove(get(), handler));
                    }
                }
                int id = nextId++;
                undo.Add(off);
                undoById[id] = off;
                return DynValue.NewNumber(id);
            }
        }
    }
}
