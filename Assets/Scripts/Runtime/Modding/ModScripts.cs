// ModScripts.cs
// Mod scripts (remake): a mod's Lua files in scripts/ (*.lua), run by MoonSharp (Assets/Plugins/MoonSharp, a Lua 5.2
// interpreter in C#: IL2CPP can't load C# compiled at runtime, an interpreter runs everywhere the game does).
//   Files      every .lua directly in scripts/ runs when the mod starts (main.lua first, the others by name); files in
//              subfolders are modules for require ("lib.util" = scripts/lib/util.lua, "othermod:lib.util" a dependency's).
//   One Lua state per mod (Vm), started when the mod becomes active and stopped when it no longer is (Sync, on every
//              ModManager.Revision change: a toggle, a session, the Mod Builder saving a file), everything it hooked put back.
//   Approval   scripts reach every game class, so a mod's scripts run only once the player allowed them (the mod
//              browser asks when the mod is turned on): PlayerPrefs "mods_scripts_ok" holds "id#scripthash", so changed
//              script files ask again. The Mod Builder's own project, the base content and a dedicated server need none.
//   Sandbox    ScriptAccess (ModScriptsSandbox.cs): the game's classes and Unity's scene classes, no .NET (files,
//              network, processes, reflection). Best effort: see its header.
//   API        the global "game" (ModScriptsApi.cs) and what the layers raise (Raise): update, scene, database, ...
//   Errors     a script error is the mod's warning (the browser, the builder's console) and never an exception in the
//              log; a call that runs too long (CallBudget Lua instructions) is stopped, a handler failing five times
//              in a row is dropped.
// Plain statics driven by ModScriptDriver (a hidden object: Update -> Tick, scene loads); the tests call them directly.

using System;
using System.Collections.Generic;
using System.Linq;
using System.Security.Cryptography;
using System.Text;
using GoF2Remake.Data;
using MoonSharp.Interpreter;
using MoonSharp.Interpreter.Loaders;
using UnityEngine;

namespace GoF2Remake.Modding
{
    [Unity.Scripting.LifecycleManagement.NoAutoStaticsCleanup]
    public static partial class ModScripts
    {
        public const string Folder = "scripts", Extension = ".lua", MainFile = "main.lua";
        const string ApprovedKey = "mods_scripts_ok";
        /// <summary>Lua instructions one call from the game may run (an endless loop would freeze the game).</summary>
        public const int CallBudget = 10000000;
        const int MaxFailures = 5, MaxLines = 400, MaxWarnings = 12;

        static readonly List<Vm> vms = new List<Vm>();
        static string runningSignature = "";
        static int seenRevision = -1;
        static bool syncing;

        [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.SubsystemRegistration)]
        static void ResetStatics()
        {
            StopAll();
            seenRevision = -1;
            lines.Clear();
            messages.Clear();
            Logged = null;
        }

        // ---- a mod's script files ----------------------------------------------------------------------------------

        /// <summary>Every .lua under scripts/ (subfolders too), the mod's own spelling, sorted.</summary>
        public static List<string> Files(ModInfo mod)
        {
            var l = new List<string>();
            if (mod?.Source == null) return l;
            foreach (var f in mod.Source.Files)
                if (f.StartsWith(Folder + "/", StringComparison.OrdinalIgnoreCase) && f.EndsWith(Extension, StringComparison.OrdinalIgnoreCase)) l.Add(f);
            l.Sort(StringComparer.OrdinalIgnoreCase);
            return l;
        }

        /// <summary>The mod runs Lua: script files, or an event graph / quest with a Lua step (LuaEventFiles).</summary>
        public static bool HasScripts(ModInfo mod) => Files(mod).Count > 0 || LuaEventFiles(mod).Count > 0;

        static readonly System.Runtime.CompilerServices.ConditionalWeakTable<ModInfo, List<string>> luaEvents =
            new System.Runtime.CompilerServices.ConditionalWeakTable<ModInfo, List<string>>();
        static readonly System.Text.RegularExpressions.Regex LuaStep =
            new System.Text.RegularExpressions.Regex("\"type\"\\s*:\\s*\"lua\"|EventLuaNode", System.Text.RegularExpressions.RegexOptions.CultureInvariant);

        /// <summary>The mod's event graphs and quests (events/) that hold a Lua step (the graphs' Lua node, the quests' "lua"
        /// step): code like the script files, so the player allows them the same way. Read once per scan of the mod.</summary>
        public static List<string> LuaEventFiles(ModInfo mod)
        {
            if (mod?.Source == null) return new List<string>();
            if (luaEvents.TryGetValue(mod, out var known)) return known;
            var l = new List<string>();
            foreach (var f in mod.Source.Files)
            {
                if (!f.StartsWith("events/", StringComparison.OrdinalIgnoreCase)) continue;
                string text;
                try { text = mod.Source.ReadText(f); }
                catch (Exception e) when (ModFileException.IsFileError(e)) { continue; }
                if (text != null && LuaStep.IsMatch(text)) l.Add(f);
            }
            l.Sort(StringComparer.OrdinalIgnoreCase);
            luaEvents.Add(mod, l);
            return l;
        }

        /// <summary>What the approval covers: the script files, and for a mod that runs Lua every events/ file that names
        /// "lua" at all (a Lua step, or a command line typed as one: a later version can't slip code past the question).</summary>
        static List<string> HashedFiles(ModInfo mod)
        {
            var l = Files(mod);
            if (mod?.Source == null || (l.Count == 0 && LuaEventFiles(mod).Count == 0)) return l;
            foreach (var f in mod.Source.Files)
            {
                if (!f.StartsWith("events/", StringComparison.OrdinalIgnoreCase)) continue;
                string text;
                try { text = mod.Source.ReadText(f); }
                catch (Exception e) when (ModFileException.IsFileError(e)) { text = "lua"; }
                if (text != null && text.IndexOf("lua", StringComparison.OrdinalIgnoreCase) >= 0) l.Add(f);
            }
            l.Sort(StringComparer.OrdinalIgnoreCase);
            return l;
        }

        /// <summary>An event's Lua step (EventRunner's "lua" line; the graphs' Lua node, the quests' Lua step): 'code' run in
        /// the scripts of the mod the event belongs to, with the table "event" as its argument (name, get(expression),
        /// set(variable, number)). The number or true / false it returns is 'result' (null: nothing). False with 'error'
        /// when that mod's scripts aren't running (not allowed yet, or no such mod) or the code failed.</summary>
        public static bool RunCode(string modId, string code, string eventName, Func<string, double> value, Action<string, double> set,
                                   out double? result, out string error)
        {
            result = null;
            error = null;
            Sync();
            var vm = vms.Find(v => string.Equals(v.mod.Id, modId, StringComparison.OrdinalIgnoreCase));
            if (vm == null) { error = $"a Lua step needs the scripts of the mod \"{modId}\" to be allowed (the Mods screen)"; return false; }
            return vm.RunCode(code, eventName, value, set, out result, out error);
        }

        /// <summary>The files that run by themselves: those directly in scripts/, main.lua first.</summary>
        public static List<string> StartFiles(ModInfo mod)
        {
            var l = Files(mod).Where(f => f.IndexOf('/', Folder.Length + 1) < 0).ToList();
            int main = l.FindIndex(f => f.EndsWith("/" + MainFile, StringComparison.OrdinalIgnoreCase));
            if (main > 0) { var m = l[main]; l.RemoveAt(main); l.Insert(0, m); }
            return l;
        }

        /// <summary>SHA-256 of the script files (paths and contents), first 12 hex digits: what the player allowed.</summary>
        public static string ScriptHash(ModInfo mod)
        {
            using var sha = SHA256.Create();
            foreach (var f in HashedFiles(mod))
            {
                var p = Encoding.UTF8.GetBytes(f.ToLowerInvariant() + "\n");
                sha.TransformBlock(p, 0, p.Length, null, 0);
                byte[] b;
                try { b = mod.Source.ReadBytes(f) ?? Array.Empty<byte>(); }
                catch (Exception e) when (ModFileException.IsFileError(e)) { b = Encoding.UTF8.GetBytes("damaged"); }
                sha.TransformBlock(b, 0, b.Length, null, 0);
            }
            sha.TransformFinalBlock(Array.Empty<byte>(), 0, 0);
            var sb = new StringBuilder();
            for (int i = 0; i < 6; i++) sb.Append(sha.Hash[i].ToString("x2"));
            return sb.ToString();
        }

        // ---- approval ----------------------------------------------------------------------------------------------

        static HashSet<string> Approved =>
            new HashSet<string>(PlayerPrefs.GetString(ApprovedKey, "").Split(new[] { ';' }, StringSplitOptions.RemoveEmptyEntries), StringComparer.OrdinalIgnoreCase);

        /// <summary>Nobody is there to ask: a dedicated server runs the scripts of the mods its operator installed (NetMods).</summary>
        public static bool AutoApprove { get; set; }

        /// <summary>The tests' approvals (never the player's PlayerPrefs); null = the player's.</summary>
        internal static HashSet<string> ApprovedOverride;

        /// <summary>The player allowed these script files to run (the mod browser's question), or nobody has to.</summary>
        public static bool IsApproved(ModInfo mod)
        {
            if (mod == null || mod.Broken) return false;
            if (mod.IsBase || mod == ModManager.BuilderProject || AutoApprove) return true;
            return (ApprovedOverride ?? Approved).Contains(mod.Id + "#" + ScriptHash(mod));
        }

        /// <summary>The player's yes (or its withdrawal) for the mod's script files as they are now.</summary>
        public static void SetApproved(ModInfo mod, bool on)
        {
            if (mod == null) return;
            var set = ApprovedOverride ?? Approved;
            set.RemoveWhere(s => s.StartsWith(mod.Id + "#", StringComparison.OrdinalIgnoreCase));
            if (on) set.Add(mod.Id + "#" + ScriptHash(mod));
            if (ApprovedOverride == null) { PlayerPrefs.SetString(ApprovedKey, string.Join(";", set)); PlayerPrefs.Save(); }
            seenRevision = -1;
        }

        /// <summary>The active mods whose scripts wait for the player's yes (the browser's and the session's warning).</summary>
        public static List<ModInfo> Unapproved(IEnumerable<ModInfo> mods) =>
            (mods ?? Enumerable.Empty<ModInfo>()).Where(m => m != null && !m.Broken && HasScripts(m) && !IsApproved(m)).ToList();

        // ---- running -----------------------------------------------------------------------------------------------

        /// <summary>How often the scripts were started afresh (the Mod Builder's Save &amp; run: a changed file restarted them already).</summary>
        public static int Starts { get; private set; }

        /// <summary>Scripts run now (cheap: what Raise checks first).</summary>
        public static bool AnyRunning => vms.Count > 0;

        /// <summary>The mods whose scripts run, in load order.</summary>
        public static IEnumerable<string> RunningIds => vms.Select(v => v.mod.Id);

        /// <summary>The running states follow the active mods: nothing when they are the same script files as before, else
        /// every state stops (its hooks put back, last started first) and the active mods' scripts start afresh.</summary>
        public static void Sync(bool force = false)
        {
            if (syncing) return;
            if (!force && seenRevision == ModManager.Revision) return;
            syncing = true;
            try
            {
                // (the revision is read first: starting the mods may read the mod lists, which can bump it)
                seenRevision = ModManager.Revision;
                var run = ModManager.Active.Where(m => !m.Broken && HasScripts(m) && IsApproved(m)).ToList();
                string signature = string.Join(";", run.Select(m => m.Id + "#" + ScriptHash(m) + "@" + m.Source.Location));
                if (!force && signature == runningSignature)
                {
                    // The same script files, read again (a rescan makes new ModInfos and closes the old ones' zips): the
                    // states go on, reading from and warning on the new entries.
                    for (int i = 0; i < vms.Count && i < run.Count; i++) vms[i].Rebind(run[i]);
                    return;
                }
                StopAll();
                runningSignature = signature;
                Starts++;
                foreach (var m in run) vms.Add(new Vm(m));
                foreach (var vm in vms.ToArray()) vm.Start();
                seenRevision = ModManager.Revision;
            }
            finally { syncing = false; }
        }

        /// <summary>Every state stopped: "stop" raised, the hooks put back in reverse order.</summary>
        public static void StopAll()
        {
            for (int i = vms.Count - 1; i >= 0; i--) vms[i].Stop();
            vms.Clear();
            runningSignature = "";
        }

        /// <summary>An event for every running script's game.on handlers; the arguments are the game's own objects (what a
        /// script may not see arrives as nil). Main thread only.</summary>
        public static void Raise(string name, params object[] args)
        {
            if (!syncing) Sync();
            if (vms.Count == 0) return;
            for (int i = 0; i < vms.Count; i++) vms[i].Raise(name, args);
        }

        /// <summary>Every frame (ModScriptDriver): the timers and "update" with the frame's seconds (0 while paused).</summary>
        public static void Tick(float dt)
        {
            Sync();
            ShowMessages();
            if (vms.Count == 0) return;
            for (int i = 0; i < vms.Count; i++) vms[i].Tick(dt);
        }

        // ---- game.saved (the save file) ----------------------------------------------------------------------------

        /// <summary>SaveGame.Capture: each running mod's game.saved table as JSON into Session.ScriptData.</summary>
        public static void CaptureSaved()
        {
            if (vms.Count == 0) return;
            Raise("saving");
            foreach (var vm in vms) vm.CaptureSaved();
        }

        /// <summary>A save was loaded or a new game began: each running mod's game.saved is that game's; 'loaded' raises
        /// "gameLoaded".</summary>
        public static void RestoreSaved(bool loaded)
        {
            if (vms.Count == 0) return;
            foreach (var vm in vms) vm.RestoreSaved();
            if (loaded) Raise("gameLoaded");
        }

        // ---- the console (the Mod Builder's script editor, the browser's warnings) ------------------------------------

        public struct Line { public string mod, text; public bool error; }

        static readonly List<Line> lines = new List<Line>();
        public static IReadOnlyList<Line> Lines => lines;
        /// <summary>A line was added to Lines (the builder's console).</summary>
        public static event Action Logged;

        public static void ClearLines() { lines.Clear(); Logged?.Invoke(); }

        static void AddLine(ModInfo mod, string text, bool error)
        {
            if (lines.Count >= MaxLines) lines.RemoveRange(0, lines.Count - MaxLines + 1);
            lines.Add(new Line { mod = mod?.Id ?? "", text = text ?? "", error = error });
            if (error)
            {
                Debug.LogWarning($"Mod script {mod?.Id}: {text}");
                string warning = "Script: " + text;
                if (mod != null && mod.Warnings.Count < 60 && mod.Warnings.Count(w => w.StartsWith("Script: ", StringComparison.Ordinal)) < MaxWarnings
                    && !mod.Warnings.Contains(warning)) mod.Warnings.Add(warning);
            }
            else Debug.Log($"Mod script {mod?.Id}: {text}");
            Logged?.Invoke();
        }

        /// <summary>The Lua syntax error in a script's text ("file:(line,column): what"), null = it compiles. Runs nothing.</summary>
        public static string SyntaxError(string code, string name)
        {
            try
            {
                ScriptAccess.Install();
                new Script(CoreModules.None).LoadString(code ?? "", null, name ?? "script");
                return null;
            }
            catch (SyntaxErrorException e) { return e.DecoratedMessage ?? e.Message; }
            catch (Exception e) { return e.Message; }
        }

        // ---- one mod's Lua state -----------------------------------------------------------------------------------

        sealed class Handler { public DynValue fn; public int id, failures; public bool removed; }
        sealed class Timer { public DynValue fn; public int id; public float left, every; public bool removed; }

        sealed partial class Vm : IScriptLoader
        {
            public ModInfo mod;
            Script script;
            Table saved;
            readonly Dictionary<string, List<Handler>> handlers = new Dictionary<string, List<Handler>>(StringComparer.Ordinal);
            readonly List<Timer> timers = new List<Timer>();
            readonly List<Action> undo = new List<Action>();
            readonly Dictionary<int, Action> undoById = new Dictionary<int, Action>();
            readonly Stack<MoonSharp.Interpreter.Coroutine> spare = new Stack<MoonSharp.Interpreter.Coroutine>();   // finished, for Call to reuse
            int nextId = 1;
            bool stopped;

            public Vm(ModInfo mod) { this.mod = mod; }

            public void Start()
            {
                ScriptAccess.Install();
                mod.Warnings.RemoveAll(w => w.StartsWith("Script: ", StringComparison.Ordinal));
                script = new Script(CoreModules.Preset_SoftSandbox | CoreModules.LoadMethods);
                script.Options.ScriptLoader = this;
                script.Options.DebugPrint = s => AddLine(mod, s, false);
                byScript[script] = this;
                saved = new Table(script);
                BuildApi();
                RestoreSaved();
                var require = script.Globals.Get("require");
                foreach (string file in StartFiles(mod))
                {
                    if (stopped) break;
                    string module = file.Substring(Folder.Length + 1, file.Length - Folder.Length - 1 - Extension.Length);
                    Call(require, DynValue.NewString(module));
                }
            }

            /// <summary>The mods were read again: the same mod's new entry (its script warnings carried over).</summary>
            public void Rebind(ModInfo now)
            {
                if (ReferenceEquals(now, mod) || now == null) return;
                foreach (string w in mod.Warnings)
                    if (w.StartsWith("Script: ", StringComparison.Ordinal) && !now.Warnings.Contains(w)) now.Warnings.Add(w);
                mod = now;
            }

            public void Stop()
            {
                if (stopped) return;
                RaiseOwn("stop");
                CaptureSaved();   // a restart (the mods changed) keeps game.saved as it is now
                stopped = true;
                for (int i = undo.Count - 1; i >= 0; i--)
                    try { undo[i](); } catch (Exception e) { Debug.LogWarning($"Mod script {mod.Id}: undoing a hook: {e.Message}"); }
                undo.Clear(); undoById.Clear(); handlers.Clear(); timers.Clear(); spare.Clear();
                StopMore();   // its components, textures, waiting functions and commands; game.config written
                if (script != null) byScript.Remove(script);
            }

            // ---- calling into Lua ----

            public void Error(string text) => AddLine(mod, text, true);

            /// <summary>A Lua function called from the game: within the instruction budget, an error the mod's warning.
            /// ok = it ran to its end, or stopped at a game.wait to go on later (a Routine).</summary>
            public DynValue Call(DynValue fn, out bool ok, params DynValue[] args) => Call(fn, out ok, true, args);

            /// <summary>'mayWait' false: the caller needs the answer now (a hook, a command, a condition), so a game.wait
            /// in it is an error.</summary>
            public DynValue Call(DynValue fn, out bool ok, bool mayWait, params DynValue[] args)
            {
                ok = false;
                if (stopped || fn == null || (fn.Type != DataType.Function && fn.Type != DataType.ClrFunction)) return DynValue.Nil;
                MoonSharp.Interpreter.Coroutine co = null;
                try
                {
                    // A new coroutine allocates two 131072-entry stacks (~2 MB, ~0.2 ms): every handler, timer and patch
                    // call made one, the GC stalls of Discord's 12-handler mod (2026-11-28). A finished one is reused.
                    co = fn.Type == DataType.Function && spare.Count > 0
                        ? script.RecycleCoroutine(spare.Pop(), fn).Coroutine : script.CreateCoroutine(fn).Coroutine;
                    co.AutoYieldCounter = CallBudget;
                    var result = co.Resume(args);
                    if (co.State == CoroutineState.ForceSuspended) { Error("a call ran for too long and was stopped (an endless loop?)"); return DynValue.Nil; }
                    if (co.State == CoroutineState.Suspended)
                    {
                        if (!mayWait) { Error("game.wait can't be used here (a hook, a command or a condition answers at once): start a game.run"); return DynValue.Nil; }
                        Adopt(co, result);   // game.wait / game.waitUntil: the rest of the function runs later
                        ok = true;
                        return DynValue.Nil;
                    }
                    ok = true;
                    return result ?? DynValue.Nil;
                }
                catch (InterpreterException e) { Error(e.DecoratedMessage ?? e.Message); }
                catch (Exception e)
                {
                    // (a game method that threw under a script's call: its message, never the log's exception counter)
                    var inner = e is System.Reflection.TargetInvocationException t && t.InnerException != null ? t.InnerException : e;
                    Error($"{inner.GetType().Name}: {inner.Message}");
                }
                finally
                {
                    // (a suspended one is a Routine now, a nested call's may still run: only a dead Lua one is kept)
                    if (co != null && co.State == CoroutineState.Dead && co.Type == MoonSharp.Interpreter.Coroutine.CoroutineType.Coroutine && spare.Count < 2) spare.Push(co);
                }
                return DynValue.Nil;
            }

            public DynValue Call(DynValue fn, params DynValue[] args) => Call(fn, out _, args);

            /// <summary>A game object as a Lua value; nil for what scripts may not see.</summary>
            public DynValue ToLua(object o)
            {
                if (o == null) return DynValue.Nil;
                if (o is DynValue d) return d;
                try { return DynValue.FromObject(script, o); }
                catch (Exception) { return DynValue.Nil; }
            }

            // ---- events and timers ----

            public void Raise(string name, object[] args)
            {
                if (stopped || !handlers.TryGetValue(name, out var list) || list.Count == 0) return;
                var lua = new DynValue[args?.Length ?? 0];
                for (int i = 0; i < lua.Length; i++) lua[i] = ToLua(args[i]);
                Run(name, list, lua);
            }

            void RaiseOwn(string name) => Raise(name, null);

            void Run(string name, List<Handler> list, DynValue[] args)
            {
                int n = list.Count;   // handlers added by a handler wait for the next time
                for (int i = 0; i < n && i < list.Count && !stopped; i++)
                {
                    var h = list[i];
                    if (h.removed) continue;
                    Call(h.fn, out bool ok, args);
                    if (ok) h.failures = 0;
                    else if (++h.failures >= MaxFailures)
                    {
                        h.removed = true;
                        Error($"a \"{name}\" handler failed {MaxFailures} times in a row and was removed");
                    }
                }
                list.RemoveAll(h => h.removed);
            }

            public void Tick(float dt)
            {
                if (stopped) return;
                if (timers.Count > 0 && dt > 0f)
                {
                    int n = timers.Count;
                    for (int i = 0; i < n && i < timers.Count && !stopped; i++)
                    {
                        var t = timers[i];
                        if (t.removed || (t.left -= dt) > 0f) continue;
                        if (t.every > 0f) t.left += Mathf.Max(t.every, 0.01f); else t.removed = true;
                        Call(t.fn, out bool ok);
                        if (!ok && t.every > 0f) { t.removed = true; Error("a repeating timer failed and was cancelled"); }
                    }
                    timers.RemoveAll(t => t.removed);
                }
                TickRoutines(dt);
                if (handlers.TryGetValue("update", out var list) && list.Count > 0) Run("update", list, new[] { DynValue.NewNumber(dt) });
            }

            // ---- game.saved ----

            public void CaptureSaved()
            {
                if (stopped || saved == null) return;
                try
                {
                    string json = MoonSharp.Interpreter.Serialization.Json.JsonTableConverter.TableToJson(saved);
                    if (string.IsNullOrEmpty(json) || json == "{}" || json == "[]") Session.ScriptData.Remove(mod.Id);
                    else Session.ScriptData[mod.Id] = json;
                }
                catch (Exception e) { Error("game.saved can't be written: " + e.Message); }
            }

            public void RestoreSaved()
            {
                if (stopped || saved == null) return;
                saved.Clear();
                if (!Session.ScriptData.TryGetValue(mod.Id, out string json) || string.IsNullOrEmpty(json)) return;
                try
                {
                    // (MoonSharp 3: ParseString; a table or a value holding one, by version)
                    object parsed = MoonSharp.Interpreter.Serialization.Json.JsonTableConverter.ParseString(json, script, false);
                    var t = parsed as Table ?? (parsed as DynValue)?.Table;
                    if (t != null) foreach (var pair in t.Pairs) saved.Set(pair.Key, pair.Value);
                }
                catch (Exception e) { Error("game.saved can't be read: " + e.Message); }
            }

            // ---- require (IScriptLoader): the mod's own scripts/, "othermod:name" an active dependency's ----

            /// <summary>"lib.util" / "lib/util" / "other:lib.util" -> "modid:scripts/lib/util.lua", null = no such file.</summary>
            string Find(string module)
            {
                module = (module ?? "").Trim();
                ModInfo from = mod;
                int colon = module.IndexOf(':');
                if (colon > 0)
                {
                    string id = module.Substring(0, colon);
                    module = module.Substring(colon + 1);
                    from = string.Equals(id, mod.Id, StringComparison.OrdinalIgnoreCase) ? mod
                        : ModManager.Active.FirstOrDefault(m => string.Equals(m.Id, id, StringComparison.OrdinalIgnoreCase) && !m.Broken && IsApproved(m));
                    if (from == null) return null;
                }
                if (module.EndsWith(Extension, StringComparison.OrdinalIgnoreCase)) module = module.Substring(0, module.Length - Extension.Length);
                string path = Folder + "/" + module.Replace('\\', '/').Replace('.', '/').TrimStart('/') + Extension;
                if (path.Contains("/../") || path.Contains("//")) return null;
                foreach (var f in from.Source.Files)
                    if (string.Equals(f, path, StringComparison.OrdinalIgnoreCase)) return from.Id + ":" + f;
                return null;
            }

            public string ResolveModuleName(string modname, Table globalContext) => Find(modname);

            public string ResolveFileName(string filename, Table globalContext) => Find(filename) ?? filename;

            public object LoadFile(string file, Table globalContext)
            {
                int colon = (file ?? "").IndexOf(':');
                var from = colon > 0 ? (string.Equals(file.Substring(0, colon), mod.Id, StringComparison.OrdinalIgnoreCase) ? mod
                    : ModManager.Active.FirstOrDefault(m => string.Equals(m.Id, file.Substring(0, colon), StringComparison.OrdinalIgnoreCase))) : null;
                string text = null;
                try { text = from?.Source.ReadText(file.Substring(colon + 1)); }
                catch (Exception e) when (ModFileException.IsFileError(e)) { throw new ScriptRuntimeException(e.Message); }
                if (text == null) throw new ScriptRuntimeException($"no script file \"{file}\" (scripts load from the mod's {Folder}/ folder only)");
                return text;
            }
        }

        /// <summary>The state a Lua value belongs to (the delegates made from Lua functions report their errors to it).</summary>
        static readonly Dictionary<Script, Vm> byScript = new Dictionary<Script, Vm>();
    }
}
