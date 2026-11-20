// ModScriptsPatch.cs
// Mod scripts: game.patch(target, "Method", function(original, [self,] ...) ... end [, "ParamType, ParamType"]) puts a Lua
// function in the place of any method of the game's classes, public or not (Core/PatchRuntime.cs: the build weaves the
// way in, so it works under IL2CPP too). The function gets what it replaced first ("original": an earlier patch, else the
// method's own code), then the object (not for a static method), then the arguments; it returns the result, then the
// method's ref / out values in order, as original does. Rules (the bridge's, ScriptAccess):
//   - the class must be one scripts can use; a method that takes or returns a file or address can't be patched;
//   - a value of a type scripts can't hold arrives as nil and passes through unchanged (an argument: the caller's; such a
//     result: the original's when the function called it);
//   - off the main thread, after game.off or once the scripts stopped the method runs as it was;
//   - an error in the function leaves the method's own behaviour (run once: not again when the function already called
//     original); 5 errors in a row take the patch out. An exception of the method itself goes on to its caller.

using System;
using System.Collections.Generic;
using System.Linq;
using System.Reflection;
using System.Runtime.ExceptionServices;
using GoF2Remake.Patching;
using MoonSharp.Interpreter;

namespace GoF2Remake.Modding
{
    public static partial class ScriptAccess
    {
        /// <summary>A ref / out parameter whose value goes back to the caller (not "in").</summary>
        public static bool GoesBack(ParameterInfo p) => p.ParameterType.IsByRef && !(p.IsIn && !p.IsOut);

        static string ParameterTypes(MethodBase m) => string.Join(", ", m.GetParameters().Select(p => TypeName(p.ParameterType)));

        static string Squeeze(string s) => new string((s ?? "").Where(c => !char.IsWhiteSpace(c)).ToArray());

        /// <summary>A text result or argument named like a path: a patch could send the game's files elsewhere.</summary>
        static bool HandlesPath(MethodInfo m)
        {
            if (TakesPath(m)) return true;
            if (m.ReturnType != typeof(string) && m.ReturnType != typeof(string[])) return false;
            foreach (string w in Words(m.Name)) if (PathWords.Contains(w)) return true;
            return false;
        }

        /// <summary>The method game.patch names: the nearest class up the chain that declares 'name'; 'signature' (the
        /// parameter types as game.describe prints them, "Database, int") picks among overloads.</summary>
        public static MethodInfo PatchTarget(Type type, string name, string signature)
        {
            if (!Allowed(type)) throw new ScriptRuntimeException($"{TypeName(type)} isn't a class scripts can use");
            var found = new List<MethodInfo>();
            foreach (var t in Chain(type))
            {
                found.AddRange(t.GetMethods(AnyMember).Where(m => m.Name == name));
                if (found.Count > 0) break;
            }
            if (found.Count == 0) throw new ScriptRuntimeException($"{TypeName(type)} has no method \"{name}\"");
            string overloads = string.Join(" | ", found.Select(m => "\"" + ParameterTypes(m) + "\""));
            if (signature != null)
            {
                string want = Squeeze(signature);
                found = found.Where(m => string.Equals(Squeeze(ParameterTypes(m)), want, StringComparison.OrdinalIgnoreCase)).ToList();
                if (found.Count == 0) throw new ScriptRuntimeException($"{TypeName(type)}.{name} has no overload ({signature}): it has {overloads}");
            }
            if (found.Count > 1)
                throw new ScriptRuntimeException($"{TypeName(type)}.{name} has {found.Count} overloads: name the parameter types as a 4th argument, one of {overloads}");
            var method = found[0];
            if (HandlesPath(method)) throw new ScriptRuntimeException($"{TypeName(type)}.{name} handles a file or address: scripts can't patch it");
            if (!PatchRuntime.CanPatch(method, out string why)) throw new ScriptRuntimeException($"{TypeName(type)}.{name} can't be patched: {why}");
            return method;
        }
    }

    public static partial class ScriptAccess
    {
        /// <summary>game.style: one inline style of an interface element from a Lua value. A number (pixels, or the plain
        /// number of opacity / flexGrow ...), "50%", "auto" / "none" / "initial", a colour ("#ff8800", "red", a Color), an
        /// enum's name ("absolute", "row", "none"), a Texture2D for backgroundImage; nil takes the style off again.</summary>
        public static void SetStyle(UnityEngine.UIElements.IStyle style, string name, DynValue v)
        {
            var p = typeof(UnityEngine.UIElements.IStyle).GetProperty(name ?? "", BindingFlags.Public | BindingFlags.Instance);
            if (p == null || !p.CanWrite) throw new ScriptRuntimeException($"game.style: there is no style \"{name}\" (top, left, width, color, fontSize, position, display ...)");
            var type = p.PropertyType;
            object Keyword(UnityEngine.UIElements.StyleKeyword k) => Activator.CreateInstance(type, k);
            string text = v.Type == DataType.String ? v.String.Trim() : null;
            object value = null;
            if (v.IsNil()) value = Keyword(UnityEngine.UIElements.StyleKeyword.Null);
            else if (type.IsGenericType && type.GetGenericTypeDefinition() == typeof(UnityEngine.UIElements.StyleEnum<>))
            {
                var e = type.GetGenericArguments()[0];
                object member = text != null ? (Enum.GetNames(e).FirstOrDefault(n => string.Equals(n, text, StringComparison.OrdinalIgnoreCase)) is string n0 ? Enum.Parse(e, n0) : null)
                              : v.Type == DataType.Number ? Enum.ToObject(e, (int)v.Number) : v.ToObject(e);
                if (member == null) throw new ScriptRuntimeException($"game.style: {name} is one of {string.Join(", ", Enum.GetNames(e))}");
                value = Activator.CreateInstance(type, member);
            }
            else if (text != null && Enum.TryParse(text, true, out UnityEngine.UIElements.StyleKeyword keyword) && keyword != UnityEngine.UIElements.StyleKeyword.Undefined)
                value = Keyword(keyword);
            else if (type == typeof(UnityEngine.UIElements.StyleLength))
            {
                if (v.Type == DataType.Number) value = new UnityEngine.UIElements.StyleLength((float)v.Number);
                else if (text != null && text.EndsWith("%", StringComparison.Ordinal) && float.TryParse(text.TrimEnd('%'), System.Globalization.NumberStyles.Float, System.Globalization.CultureInfo.InvariantCulture, out float percent))
                    value = new UnityEngine.UIElements.StyleLength(new UnityEngine.UIElements.Length(percent, UnityEngine.UIElements.LengthUnit.Percent));
                else if (text != null && float.TryParse(text.Replace("px", ""), System.Globalization.NumberStyles.Float, System.Globalization.CultureInfo.InvariantCulture, out float px))
                    value = new UnityEngine.UIElements.StyleLength(px);
            }
            else if (type == typeof(UnityEngine.UIElements.StyleFloat) && v.Type == DataType.Number) value = new UnityEngine.UIElements.StyleFloat((float)v.Number);
            else if (type == typeof(UnityEngine.UIElements.StyleInt) && v.Type == DataType.Number) value = new UnityEngine.UIElements.StyleInt((int)v.Number);
            else if (type == typeof(UnityEngine.UIElements.StyleColor))
            {
                if (text != null) { if (UnityEngine.ColorUtility.TryParseHtmlString(text, out var c)) value = new UnityEngine.UIElements.StyleColor(c); }
                else if (v.Type == DataType.UserData && v.UserData.Object is UnityEngine.Color colour) value = new UnityEngine.UIElements.StyleColor(colour);
            }
            else if (type == typeof(UnityEngine.UIElements.StyleBackground) && v.Type == DataType.UserData && v.UserData.Object is UnityEngine.Texture2D texture)
                value = new UnityEngine.UIElements.StyleBackground(texture);
            if (value == null) throw new ScriptRuntimeException($"game.style: {name} can't take {(text != null ? "\"" + text + "\"" : v.Type.ToString().ToLowerInvariant())}");
            p.SetValue(style, value);
        }
    }

    public static partial class ModScripts
    {
        sealed partial class Vm
        {
            const int PatchFailures = 5;

            /// <summary>game.patch: see the file's head. Returns the handle game.off takes.</summary>
            DynValue Patch(CallbackArguments a)
            {
                Target(a, "game.patch", out _, out Type type);
                string name = a.AsType(1, "game.patch", DataType.String).String;
                var fn = a.AsType(2, "game.patch", DataType.Function);
                var m = ScriptAccess.PatchTarget(type, name, a.Count > 3 && a[3].Type == DataType.String ? a[3].String : null);
                var ps = m.GetParameters();
                bool isStatic = m.IsStatic, returns = m.ReturnType != typeof(void) && ScriptAccess.Usable(m.ReturnType);
                var usable = ps.Select(p => ScriptAccess.Usable(p.ParameterType)).ToArray();
                string label = ScriptAccess.TypeName(m.DeclaringType) + "." + name;
                bool removed = false;
                int failures = 0;
                IDisposable patch = null;
                Action off = () => { removed = true; patch?.Dispose(); };

                patch = PatchRuntime.Add(m, (original, self, args) =>
                {
                    if (removed || stopped || !ScriptAccess.OnMainThread) return original(self, args);
                    bool called = false;
                    object lastResult = null;
                    object[] lastArgs = null;
                    ExceptionDispatchInfo thrown = null;
                    // What the Lua function calls for the code it replaced: ([self,] arguments) -> the result, the ref / out values.
                    var callOriginal = DynValue.NewCallback((context, la) =>
                    {
                        int first = isStatic ? 0 : 1;
                        object target = isStatic ? null : la.Count > 0 && la[0].Type == DataType.UserData && la[0].UserData.Object != null ? la[0].UserData.Object : self;
                        var clr = new object[ps.Length];
                        for (int i = 0; i < ps.Length; i++)
                            clr[i] = !usable[i] ? args[i]   // (a value scripts can't hold: the caller's own)
                                   : ps[i].IsOut ? null
                                   : ScriptAccess.Argument(first + i < la.Count ? la[first + i] : DynValue.Nil, ps[i].ParameterType);
                        try { lastResult = original(target, clr); }
                        catch (Exception e) { thrown = ExceptionDispatchInfo.Capture(e); throw; }
                        called = true;
                        lastArgs = clr;
                        var values = new List<DynValue> { returns ? ToLua(lastResult) : DynValue.Nil };
                        for (int i = 0; i < ps.Length; i++) if (ScriptAccess.GoesBack(ps[i])) values.Add(usable[i] ? ToLua(clr[i]) : DynValue.Nil);
                        return values.Count == 1 ? values[0] : DynValue.NewTuple(values.ToArray());
                    }, name);

                    int at = isStatic ? 1 : 2;
                    var lua = new DynValue[ps.Length + at];
                    lua[0] = callOriginal;
                    if (!isStatic) lua[1] = ToLua(self);
                    for (int i = 0; i < ps.Length; i++) lua[at + i] = usable[i] ? ToLua(args[i]) : DynValue.Nil;

                    var result = Call(fn, out bool ok, false, lua);
                    object answer = null;
                    if (ok)
                    {
                        try
                        {
                            var back = new object[ps.Length];
                            int index = 1;
                            for (int i = 0; i < ps.Length; i++)
                                if (ScriptAccess.GoesBack(ps[i]))
                                    back[i] = usable[i] ? ScriptAccess.CoerceAt(result, index++, ps[i].ParameterType.GetElementType()) : lastArgs?[i] ?? args[i];
                            answer = m.ReturnType == typeof(void) ? null : returns ? ScriptAccess.CoerceAt(result, 0, m.ReturnType) : lastResult;
                            for (int i = 0; i < ps.Length; i++) if (ScriptAccess.GoesBack(ps[i])) args[i] = back[i];
                        }
                        catch (Exception e) when (e is InterpreterException || e is InvalidCastException || e is FormatException || e is ArgumentException)
                        {
                            Error($"game.patch {label}: what the function returned doesn't fit ({e.Message})");
                            ok = false;
                        }
                    }
                    if (ok) { failures = 0; return answer; }

                    // The function failed: the method as it was. Its own exception goes on; its own run isn't repeated.
                    if (++failures >= PatchFailures && !removed)
                    {
                        Error($"game.patch {label}: taken out after {PatchFailures} errors in a row");
                        off();
                    }
                    thrown?.Throw();
                    if (!called) return original(self, args);
                    for (int i = 0; i < ps.Length; i++) if (ScriptAccess.GoesBack(ps[i])) args[i] = lastArgs[i];
                    return lastResult;
                });

                int id = nextId++;
                undo.Add(off);
                undoById[id] = off;
                return DynValue.NewNumber(id);
            }
        }
    }
}
