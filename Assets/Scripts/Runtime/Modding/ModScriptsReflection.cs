// ModScriptsReflection.cs
// Part of ScriptAccess (the mod scripts' sandbox): what MoonSharp's own bridge leaves out, done by reflection under the
// same rules (allowed classes only, no method that takes a path-like text, only types a script can hold).
//   Members by name   Field / SetField / Invoke: any member of an allowed class, public or not (game.field, game.setField,
//                     game.call), and generic methods with their type arguments named (game.generic).
//   Delegates         a Lua function wherever the game's API takes a delegate: every delegate type the game's classes
//                     use in a field, property or parameter gets a conversion (RegisterDelegateTypes), so
//                     "obj.OnDone = function() end" and "obj:Each(function(x) end)" work; Invoke converts any other.
//   out / ref         MakeDelegate (ModScriptsSandbox.cs) hands a delegate's out values back from the Lua function's
//                     extra results; Invoke returns a method's out values after its result.

using System;
using System.Collections.Generic;
using System.Linq;
using System.Reflection;
using MoonSharp.Interpreter;

namespace GoF2Remake.Modding
{
    public static partial class ScriptAccess
    {
        const BindingFlags AnyMember = BindingFlags.Public | BindingFlags.NonPublic | BindingFlags.Instance | BindingFlags.Static | BindingFlags.DeclaredOnly;

        /// <summary>A delegate's result with its out values (a hook passing the game's own call through).</summary>
        public sealed class Multi { public object[] values; }

        /// <summary>The value at 'index' of what a delegate's body returned (0 = the result, 1.. = its out / ref values, in
        /// order), as 'type'.</summary>
        public static object CoerceAt(object result, int index, Type type)
        {
            if (result is Multi m) return Coerce(index < m.values.Length ? m.values[index] : null, type);
            if (result is DynValue v)
            {
                if (v.Type == DataType.Tuple) v = index < v.Tuple.Length ? v.Tuple[index] : DynValue.Nil;
                else if (index > 0) v = DynValue.Nil;
                return FromLua(v, type);
            }
            return index == 0 ? Coerce(result, type) : Coerce(null, type);
        }

        /// <summary>A Lua value as a method's argument: a function becomes the delegate the parameter asks for.</summary>
        public static object Argument(DynValue v, Type type)
        {
            if (type.IsByRef) type = type.GetElementType();
            if (v != null && (v.Type == DataType.Function || v.Type == DataType.ClrFunction) && typeof(Delegate).IsAssignableFrom(type) && type != typeof(Delegate))
                return MakeDelegate(type, v);
            return FromLua(v, type);
        }

        static bool delegatesRegistered;

        /// <summary>Lua functions convert to every delegate type the game's own classes take or hold (once: a scan of their
        /// fields, properties and parameters), and to the plain Action / Func / Predicate / Comparison of the simple types.</summary>
        static void RegisterDelegateTypes()
        {
            if (delegatesRegistered) return;
            delegatesRegistered = true;
            var types = new HashSet<Type>
            {
                typeof(Action), typeof(UnityEngine.Events.UnityAction), typeof(Action<bool>), typeof(Action<int>), typeof(Action<float>), typeof(Action<string>),
                typeof(Action<object>), typeof(Func<bool>), typeof(Func<int>), typeof(Func<float>), typeof(Func<string>), typeof(Func<object>),
                typeof(Predicate<object>), typeof(Comparison<object>),
            };
            void Consider(Type t)
            {
                if (t == null) return;
                if (t.IsByRef) t = t.GetElementType();
                if (typeof(Delegate).IsAssignableFrom(t) && t != typeof(Delegate) && t != typeof(MulticastDelegate) && !t.ContainsGenericParameters && Usable(t)) types.Add(t);
            }
            foreach (var t in AllowedTypes(true))
            {
                try
                {
                    foreach (var f in t.GetFields(AnyMember)) Consider(f.FieldType);
                    foreach (var p in t.GetProperties(AnyMember)) Consider(p.PropertyType);
                    foreach (var m in t.GetMethods(AnyMember)) foreach (var p in m.GetParameters()) Consider(p.ParameterType);
                    foreach (var c in t.GetConstructors(AnyMember)) foreach (var p in c.GetParameters()) Consider(p.ParameterType);
                }
                catch (Exception) { }   // (a type whose members can't be read: nothing of it)
            }
            var conv = Script.GlobalOptions.CustomConverters;
            foreach (var type in types)
            {
                var d = type;
                conv.SetScriptToClrCustomConversion(DataType.Function, d, v => MakeDelegate(d, v));
            }
        }

        /// <summary>The class and its base classes a script may look into, the class first.</summary>
        static IEnumerable<Type> Chain(Type type)
        {
            for (var t = type; t != null && t != typeof(object) && t != typeof(ValueType); t = t.BaseType)
                if (Allowed(t)) yield return t;
        }

        static string Where(Type type, string name) => $"{TypeName(type)} has no \"{name}\" scripts can use";

        /// <summary>A field or property by name, public or not (game.field).</summary>
        public static object Field(object target, Type type, string name)
        {
            if (!Allowed(type)) throw new ScriptRuntimeException($"{TypeName(type)} isn't a class scripts can use");
            foreach (var t in Chain(type))
            {
                var f = t.GetField(name, AnyMember);
                if (f != null)
                {
                    if (!Usable(f.FieldType)) break;
                    if (!f.IsStatic && target == null) throw new ScriptRuntimeException($"{TypeName(type)}.{name} needs an object, not the class");
                    return f.GetValue(f.IsStatic ? null : target);
                }
                var p = t.GetProperty(name, AnyMember);
                if (p != null && p.GetIndexParameters().Length == 0 && p.GetGetMethod(true) != null)
                {
                    if (!Usable(p.PropertyType)) break;
                    var get = p.GetGetMethod(true);
                    if (!get.IsStatic && target == null) throw new ScriptRuntimeException($"{TypeName(type)}.{name} needs an object, not the class");
                    return get.Invoke(get.IsStatic ? null : target, null);
                }
            }
            throw new ScriptRuntimeException(Where(type, name));
        }

        /// <summary>A field or property set by name, public or not (game.setField).</summary>
        public static void SetField(object target, Type type, string name, DynValue value)
        {
            if (!Allowed(type)) throw new ScriptRuntimeException($"{TypeName(type)} isn't a class scripts can use");
            foreach (var t in Chain(type))
            {
                var f = t.GetField(name, AnyMember);
                if (f != null)
                {
                    if (!Usable(f.FieldType) || f.IsLiteral) break;
                    if (!f.IsStatic && target == null) throw new ScriptRuntimeException($"{TypeName(type)}.{name} needs an object, not the class");
                    f.SetValue(f.IsStatic ? null : target, Argument(value, f.FieldType));
                    return;
                }
                var p = t.GetProperty(name, AnyMember);
                if (p != null && p.GetIndexParameters().Length == 0 && p.GetSetMethod(true) != null)
                {
                    if (!Usable(p.PropertyType)) break;
                    var set = p.GetSetMethod(true);
                    if (!set.IsStatic && target == null) throw new ScriptRuntimeException($"{TypeName(type)}.{name} needs an object, not the class");
                    set.Invoke(set.IsStatic ? null : target, new[] { Argument(value, p.PropertyType) });
                    return;
                }
            }
            throw new ScriptRuntimeException(Where(type, name) + " (or it can't be set)");
        }

        /// <summary>A method by name, public or not, with the arguments given (game.call); with 'generic' its type arguments
        /// (game.generic). The first overload the arguments fit runs. The result, then the out / ref values.</summary>
        public static object[] Invoke(object target, Type type, string name, IList<DynValue> args, Type[] generic)
        {
            if (!Allowed(type)) throw new ScriptRuntimeException($"{TypeName(type)} isn't a class scripts can use");
            string problem = null;
            foreach (var t in Chain(type))
                foreach (var candidate in t.GetMethods(AnyMember))
                {
                    if (candidate.Name != name) continue;
                    var m = candidate;
                    if (m.IsGenericMethodDefinition)
                    {
                        if (generic == null || m.GetGenericArguments().Length != generic.Length) { problem ??= $"{name} needs type arguments: game.generic(target, \"{name}\", {{ \"Class\" }}, ...)"; continue; }
                        try { m = m.MakeGenericMethod(generic); } catch (ArgumentException) { continue; }
                    }
                    else if (generic != null) continue;
                    var ps = m.GetParameters();
                    int given = args.Count;
                    if (given > ps.Length || ps.Skip(given).Any(p => !p.HasDefaultValue && !p.IsOut)) { problem ??= $"{name} takes {ps.Length} argument(s), not {given}"; continue; }
                    if (IsGame(t) && TakesPath(m)) { problem = $"{name} takes a file or address: scripts can't call it"; continue; }
                    if (!Usable(m.ReturnType) || ps.Any(p => !Usable(p.ParameterType))) { problem ??= $"{name} uses a type scripts can't hold"; continue; }
                    if (!m.IsStatic && target == null) { problem ??= $"{TypeName(type)}.{name} needs an object, not the class"; continue; }
                    object[] clr = new object[ps.Length];
                    try
                    {
                        for (int i = 0; i < ps.Length; i++)
                            clr[i] = i < given ? (ps[i].IsOut ? null : Argument(args[i], ps[i].ParameterType)) : ps[i].HasDefaultValue ? ps[i].DefaultValue : null;
                    }
                    catch (Exception e) when (!(e is ScriptRuntimeException)) { problem ??= $"{name}: an argument doesn't fit ({e.Message})"; continue; }
                    catch (ScriptRuntimeException e) { problem ??= $"{name}: an argument doesn't fit ({e.Message})"; continue; }
                    object result;
                    try { result = m.Invoke(m.IsStatic ? null : target, clr); }
                    catch (TargetInvocationException e) when (e.InnerException != null) { throw new ScriptRuntimeException($"{name}: {e.InnerException.GetType().Name}: {e.InnerException.Message}"); }
                    var results = new List<object> { result };
                    for (int i = 0; i < ps.Length; i++) if (ps[i].ParameterType.IsByRef) results.Add(clr[i]);
                    return results.ToArray();
                }
            throw new ScriptRuntimeException(problem ?? Where(type, name));
        }
    }
}
