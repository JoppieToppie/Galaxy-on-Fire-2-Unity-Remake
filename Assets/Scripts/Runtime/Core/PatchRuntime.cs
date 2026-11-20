// PatchRuntime.cs
// Remake mods: any method of the game can be replaced while the game runs (the mod scripts' game.patch), on every platform.
// IL2CPP compiles the game ahead of time, so a method can't be rewritten at run time (what Harmony does); instead the build
// weaves a check into the start of every method of the game's assemblies (Scripts/Editor/CodeGen/PatchWeaver.cs, an
// ILPostProcessor, run on every compile, the Editor's too):
//     var h = PatchTable.Table[id];                    (PatchTable: one per assembly, made by the weaver)
//     if (h != null && !PatchRuntime.Skip(h)) return (T)h(this, new object[] { a, b });   (ref / out values copied back)
//     ... the method's own code ...
// A woven method carries [PatchSite(id)]. Not woven (so not patchable): constructors, generic methods and classes, a
// struct's instance methods, compiler-made code (lambdas, iterators' MoveNext, auto-properties), [AggressiveInlining]
// methods (the way to keep a hot one out), RPCs, Burst code, native callbacks, pointer and ref-struct signatures.
// The method's own code: Skip is true once for the handler about to be passed by, so calling the method again runs its
// body. An overridable method has a private __patchBase_<Name> beside it that calls it without virtual dispatch (reflection
// alone would run the override of the object's class instead).
// Names start with "Patch" on purpose: the scripts' sandbox denies such classes (ScriptAccess.GameDeniedPrefixes), so a
// script patches through game.patch and its rules only.

using System;
using System.Collections.Generic;
using System.Reflection;
using System.Runtime.ExceptionServices;
using UnityEngine;

namespace GoF2Remake.Patching
{
    /// <summary>What a patched method runs in place of its own code: (the object or null, the arguments) -> the result.</summary>
    public delegate object PatchHandler(object self, object[] args);

    /// <summary>A patch: (original, self, args) -> the result. original(self, args) runs what was there before it (an
    /// earlier patch, else the method's own code). A ref / out value is returned by writing it into 'args'.</summary>
    public delegate object PatchBody(PatchHandler original, object self, object[] args);

    /// <summary>On a method the weaver made patchable: its slot in its assembly's PatchTable.</summary>
    [AttributeUsage(AttributeTargets.Method, Inherited = false)]
    public sealed class PatchSiteAttribute : Attribute
    {
        public readonly int Id;
        public PatchSiteAttribute(int id) { Id = id; }
    }

    [Unity.Scripting.LifecycleManagement.NoAutoStaticsCleanup]
    public static class PatchRuntime
    {
        public const string TableType = "GoF2Remake.Patching.PatchTable", TableField = "Table", SkippedField = "Skipped", BasePrefix = "__patchBase_";

        sealed class Site
        {
            public MethodInfo method, invoker;
            public PatchHandler[] table;
            public int id;
            public Type[] defaults;       // per argument: the value type a null stands for (ref / out values going back), else null
            public Type returnDefault;
            public PatchBody[] chain = Array.Empty<PatchBody>();   // the oldest first; the newest runs first
            public PatchHandler dispatcher;
        }

        static readonly Dictionary<(Assembly, int), Site> sites = new Dictionary<(Assembly, int), Site>();
        [ThreadStatic] static PatchHandler skip;

        [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.SubsystemRegistration)]
        public static void RemoveAll()
        {
            foreach (var s in sites.Values) { s.chain = Array.Empty<PatchBody>(); s.table[s.id] = null; }
            sites.Clear();
        }

        /// <summary>Woven code: the method's own code is wanted this once (the handler's "original").</summary>
        public static bool Skip(PatchHandler handler)
        {
            if (!ReferenceEquals(skip, handler)) return false;
            skip = null;
            return true;
        }

        /// <summary>The number of patchable methods in a game assembly, and of those the weaver left out (0: not woven).</summary>
        public static (int sites, int skipped) Woven(Assembly assembly)
        {
            var t = assembly?.GetType(TableType);
            var table = t?.GetField(TableField, BindingFlags.Public | BindingFlags.Static)?.GetValue(null) as PatchHandler[];
            var skipped = t?.GetField(SkippedField, BindingFlags.Public | BindingFlags.Static)?.GetRawConstantValue();
            return (table?.Length ?? 0, skipped is int n ? n : 0);
        }

        /// <summary>The method was woven (PatchRuntime.cs lists what isn't); else why not, for a message.</summary>
        public static bool CanPatch(MethodInfo m, out string why)
        {
            why = null;
            if (m == null) { why = "no such method"; return false; }
            if (m.GetCustomAttribute<PatchSiteAttribute>(false) != null) return true;
            why = m.IsAbstract ? "it is abstract: patch the class that implements it"
                : m.IsGenericMethod || m.DeclaringType.IsGenericType ? "generic methods and classes can't be patched"
                : !m.IsStatic && m.DeclaringType.IsValueType ? "a struct's own methods can't be patched"
                : Woven(m.DeclaringType.Assembly).sites == 0 ? "only the game's own classes can be patched"
                : "it is an inlined, compiler-made, network (RPC) or pointer-taking method";
            return false;
        }

        /// <summary>Puts 'body' in the method's place (on top of the patches already there). Dispose the result to take it
        /// out again. Main thread only.</summary>
        public static IDisposable Add(MethodInfo m, PatchBody body)
        {
            if (body == null) throw new ArgumentNullException(nameof(body));
            if (!CanPatch(m, out string why)) throw new ArgumentException($"{m?.DeclaringType?.Name}.{m?.Name} can't be patched: {why}");
            var site = SiteOf(m);
            var chain = new PatchBody[site.chain.Length + 1];
            site.chain.CopyTo(chain, 0);
            chain[chain.Length - 1] = body;
            site.chain = chain;
            site.table[site.id] = site.dispatcher;
            return new Removal { site = site, body = body };
        }

        sealed class Removal : IDisposable
        {
            public Site site;
            public PatchBody body;
            public void Dispose()
            {
                if (site == null) return;
                int at = Array.IndexOf(site.chain, body);
                if (at >= 0)
                {
                    var chain = new PatchBody[site.chain.Length - 1];
                    Array.Copy(site.chain, 0, chain, 0, at);
                    Array.Copy(site.chain, at + 1, chain, at, chain.Length - at);
                    site.chain = chain;
                    if (chain.Length == 0) site.table[site.id] = null;
                }
                site = null;
            }
        }

        static Site SiteOf(MethodInfo m)
        {
            int id = m.GetCustomAttribute<PatchSiteAttribute>(false).Id;
            var assembly = m.DeclaringType.Assembly;
            if (sites.TryGetValue((assembly, id), out var site)) return site;
            var table = (PatchHandler[])assembly.GetType(TableType).GetField(TableField, BindingFlags.Public | BindingFlags.Static).GetValue(null);
            var ps = m.GetParameters();
            var types = new Type[ps.Length];
            var defaults = new Type[ps.Length];
            for (int i = 0; i < ps.Length; i++)
            {
                types[i] = ps[i].ParameterType;
                defaults[i] = ValueDefault(ps[i].ParameterType.IsByRef ? ps[i].ParameterType.GetElementType() : ps[i].ParameterType);
            }
            site = new Site { method = m, invoker = m, table = table, id = id, defaults = defaults, returnDefault = ValueDefault(m.ReturnType) };
            // An overridable method: its own code through the weaver's non-virtual call of it.
            if (m.IsVirtual && !m.IsFinal && !m.DeclaringType.IsSealed)
                site.invoker = m.DeclaringType.GetMethod(BasePrefix + m.Name, BindingFlags.NonPublic | BindingFlags.Instance | BindingFlags.DeclaredOnly, null, types, null) ?? m;
            site.dispatcher = (self, args) => Dispatch(site, self, args);
            sites[(assembly, id)] = site;
            return site;
        }

        /// <summary>A type whose null would crash the woven unboxing: a value type that isn't Nullable.</summary>
        static Type ValueDefault(Type t) => t != typeof(void) && t.IsValueType && Nullable.GetUnderlyingType(t) == null ? t : null;

        static object Dispatch(Site site, object self, object[] args)
        {
            object result = Run(site, site.chain, site.chain.Length - 1, self, args);
            // What the woven code unboxes must be there: a missing value is the type's default.
            if (result == null && site.returnDefault != null) result = Activator.CreateInstance(site.returnDefault);
            for (int i = 0; i < site.defaults.Length && i < args.Length; i++)
                if (args[i] == null && site.defaults[i] != null) args[i] = Activator.CreateInstance(site.defaults[i]);
            return result;
        }

        static object Run(Site site, PatchBody[] chain, int level, object self, object[] args)
        {
            if (level < 0) return Original(site, self, args);
            return chain[level]((s, a) => Run(site, chain, level - 1, s, a), self, args);
        }

        static object Original(Site site, object self, object[] args)
        {
            var handler = site.dispatcher;
            skip = handler;
            try { return site.invoker.Invoke(site.method.IsStatic ? null : self, args); }
            catch (TargetInvocationException e) when (e.InnerException != null)
            {
                ExceptionDispatchInfo.Capture(e.InnerException).Throw();   // the method's own exception, as its caller would get it
                throw;
            }
            finally { if (ReferenceEquals(skip, handler)) skip = null; }
        }
    }
}
