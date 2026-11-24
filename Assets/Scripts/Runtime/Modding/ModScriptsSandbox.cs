// ModScriptsSandbox.cs
// What a mod script may reach (ScriptAccess): "the game only".
//   Lua side   MoonSharp's soft sandbox (no io, no os but the clock, no debug) plus require / load, which read the
//              mod's own scripts/ only (ModScripts.Vm, the script loader); the platform accessor has no files.
//   C# side    objects reach Lua as userdata only when their type is allowed (the registration policy below; public
//              members, by reflection): the game's own runtime assemblies and Unity's scene namespaces, plus lists /
//              dictionaries / sets of them. Everything else (System.*, Netcode, Unity services, Newtonsoft) has no
//              Lua form: a member returning or taking such a type fails with a script error, and System.Type never
//              converts, so there is no reflection.
//   Left out   the Unity classes that reach outside the game (Application: OpenURL / Quit, PlayerPrefs, the clipboard,
//              asset bundles, web requests, the camera and microphone, Android's Java bridge) and the game classes
//              that are its doors to disk and network (file dialogs, the mod sources and importer, the patcher, the
//              update check, the web admin, the MCP server, sign-in, this class and ModScripts itself); of the other
//              game classes, the public methods taking a path-like text (path, file, folder, url ...).
// Best effort, not a proof: the game has ~125 source files that touch files or the network, and a script that tries
// hard may find a public method that lets a path through. Hence the player's approval (ModScripts.IsApproved) and the
// browser's warning: scripts are for mods whose author the player trusts.

using System;
using System.Collections.Generic;
using System.Linq;
using System.Linq.Expressions;
using System.Reflection;
using MoonSharp.Interpreter;
using MoonSharp.Interpreter.Interop;
using MoonSharp.Interpreter.Interop.RegistrationPolicies;
using MoonSharp.Interpreter.Platforms;

namespace GoF2Remake.Modding
{
    [Unity.Scripting.LifecycleManagement.NoAutoStaticsCleanup]
    public static partial class ScriptAccess
    {
        /// <summary>The game's runtime assemblies (Scripts/Runtime/README.md), not its tests or Editor tools.</summary>
        static readonly HashSet<string> GameAssemblies = new HashSet<string> { "GoF2.Core", "GoF2.Game", "GoF2.Space", "GoF2.Net", "GoF2.App" };

        static readonly HashSet<string> UnityNamespaces = new HashSet<string>
        {
            "UnityEngine", "UnityEngine.UIElements", "UnityEngine.InputSystem", "UnityEngine.InputSystem.Controls",
            "UnityEngine.SceneManagement", "UnityEngine.Rendering", "UnityEngine.Rendering.Universal", "UnityEngine.Audio",
            "UnityEngine.Events", "UnityEngine.EventSystems", "UnityEngine.UI", "UnityEngine.AI", "UnityEngine.Animations",
            "UnityEngine.Playables", "UnityEngine.TextCore", "UnityEngine.TextCore.Text", "UnityEngine.VFX", "UnityEngine.Pool",
        };

        /// <summary>Unity classes (namespace UnityEngine) that reach outside the game.</summary>
        static readonly HashSet<string> UnityDenied = new HashSet<string>
        {
            "Application", "PlayerPrefs", "GUIUtility", "ScreenCapture", "Microphone", "Caching", "Cache", "Handheld", "Input",
            "LocationService", "Ping", "Security", "Types", "Compass", "Gyroscope", "TouchScreenKeyboard", "ResourcesAPI",
            "BuildCompression", "CachedAssetBundle",
        };
        static readonly string[] UnityDeniedPrefixes = { "Android", "AssetBundle", "iPhone", "WWW", "WebCam", "Remote", "Crash", "Social", "FullScreenMovie", "Network", "Windows" };

        /// <summary>The game's own doors to disk, network and other programs (by class name, nested classes with them).</summary>
        static readonly HashSet<string> GameDenied = new HashSet<string>
        {
            "ModScripts", "ScriptAccess", "ModScriptDriver", "GamePaths", "FileDialog", "ModImport", "ModSource", "FolderSource", "ZipSource",
            "BaseContentSource", "ModTextureCache", "ModRegistry", "ImageImport", "AemImport", "OriginalModImport", "ModOptimizer", "ModdingGuide",
            "WinConsole", "ConsoleInput", "DedicatedServer", "WebAdmin", "DiscordIpc", "GamePatcher", "UpdateCheck", "AndroidUpdateInstaller",
            "IssueReport", "SteamDeckRelaunch", "HitchLogger", "LiveSplitLink", "LiveSplitRun", "SaveFolder", "SaveFolderPicker", "SaveTransfer",
            "OriginalSave", "OriginalSaveTransfer", "MacGameControllers", "PhoneVibrator", "NetLobby", "ModLodCache", "ShipShadowBaker",
            "PhotoTools", "Crc32",
        };
        static readonly string[] GameDeniedPrefixes = { "ModBuilderMcp", "ModProject", "Patch", "NetProfiles", "NetModeration" };

        /// <summary>A public game method with a text parameter named like one of these is left out (a path may go to disk).</summary>
        static readonly HashSet<string> PathWords = new HashSet<string>(StringComparer.OrdinalIgnoreCase)
        {
            "path", "paths", "file", "files", "filename", "folder", "folders", "dir", "directory", "url", "uri", "location", "archive", "zip", "root",
            "address", "endpoint", "hostname", "ip",   // (and where a connection goes: NetGame.StartClient)
        };

        static readonly HashSet<Type> Collections = new HashSet<Type>
        {
            typeof(List<>), typeof(Dictionary<,>), typeof(HashSet<>), typeof(Queue<>), typeof(Stack<>), typeof(KeyValuePair<,>),
            typeof(Dictionary<,>.KeyCollection), typeof(Dictionary<,>.ValueCollection), typeof(Nullable<>),
        };

        static bool installed;
        static int mainThread;

        /// <summary>Lua runs on the main thread only (a hooked delegate may be called from a worker).</summary>
        public static bool OnMainThread => System.Threading.Thread.CurrentThread.ManagedThreadId == mainThread;

        /// <summary>The class names left out, for the test that they still exist.</summary>
        internal static IEnumerable<string> DeniedGameNames => GameDenied;

        /// <summary>MoonSharp's process-wide settings (once): the policy below, reflection instead of generated code (IL2CPP),
        /// no file or environment access, Lua functions as the plain delegates.</summary>
        public static void Install()
        {
            if (installed) return;
            installed = true;
            mainThread = System.Threading.Thread.CurrentThread.ManagedThreadId;
            UserData.RegistrationPolicy = new Policy();
            UserData.DefaultAccessMode = InteropAccessMode.Reflection;
            Script.GlobalOptions.Platform = new LimitedPlatformAccessor();
            Script.GlobalOptions.RethrowExceptionNested = false;
            RegisterDelegateTypes();   // Lua functions as the delegates the game's classes take (ModScriptsReflection.cs)
            RegisterHidingTypes();
        }

        /// <summary>MoonSharp refuses a class that has two members of one name that can't be overloads: a property, field or
        /// event that hides a base class's with "new" (UI Toolkit's TextElement.experimental over VisualElement's, so Label,
        /// Button and every text control: "Multiple members named experimental are being added"). Such classes are described
        /// here instead, the most derived member standing for the name.</summary>
        static void RegisterHidingTypes()
        {
            foreach (var t in AllowedTypes())
            {
                if (t.IsEnum || t.IsInterface || t.IsGenericTypeDefinition || typeof(Delegate).IsAssignableFrom(t)) continue;
                try { if (Hides(t)) UserData.RegisterType(Describe(t)); }
                catch (Exception) { }   // (a class that can't be read: MoonSharp's own attempt reports it when a script asks)
            }
        }

        const BindingFlags Declared = BindingFlags.Public | BindingFlags.Instance | BindingFlags.Static | BindingFlags.DeclaredOnly;

        /// <summary>The class's property / field / event names up its chain, the most derived first.</summary>
        static IEnumerable<MemberInfo> ValueMembers(Type t)
        {
            for (var c = t; c != null && c != typeof(object); c = c.BaseType)
            {
                foreach (var p in c.GetProperties(Declared)) if (!p.IsSpecialName && p.GetIndexParameters().Length == 0) yield return p;
                foreach (var f in c.GetFields(Declared)) if (!f.IsSpecialName) yield return f;
                foreach (var e in c.GetEvents(Declared)) yield return e;
            }
        }

        static bool Hides(Type t)
        {
            var names = new HashSet<string>(StringComparer.Ordinal);
            foreach (var m in ValueMembers(t)) if (!names.Add(m.Name)) return true;
            return false;
        }

        /// <summary>What StandardUserDataDescriptor's own filling does, one member a name for what can't be overloaded.</summary>
        static IUserDataDescriptor Describe(Type t)
        {
            const InteropAccessMode Mode = InteropAccessMode.Reflection;
            var d = new StandardUserDataDescriptor(t, InteropAccessMode.HideMembers);   // (HideMembers: it fills nothing itself)
            var names = new HashSet<string>(StringComparer.Ordinal);
            foreach (var m in ValueMembers(t))
            {
                if (!names.Add(m.Name)) continue;
                if (m is PropertyInfo p) d.AddMember(p.Name, PropertyMemberDescriptor.TryCreateIfVisible(p, Mode));
                else if (m is FieldInfo f) d.AddMember(f.Name, FieldMemberDescriptor.TryCreateIfVisible(f, Mode));
                else if (m is EventInfo e) d.AddMember(e.Name, EventMemberDescriptor.TryCreateIfVisible(e, Mode));
            }
            foreach (var c in t.GetConstructors()) d.AddMember("__new", MethodMemberDescriptor.TryCreateIfVisible(c, Mode));
            foreach (var m in t.GetMethods(BindingFlags.Public | BindingFlags.Instance | BindingFlags.Static | BindingFlags.FlattenHierarchy))
            {
                if (names.Contains(m.Name) || !MethodMemberDescriptor.CheckMethodIsCompatible(m, false)) continue;
                d.AddMember(m.Name, MethodMemberDescriptor.TryCreateIfVisible(m, Mode));
            }
            return d;
        }

        sealed class Policy : IRegistrationPolicy
        {
            public bool AllowTypeAutoRegistration(Type type) => Allowed(type);

            public IUserDataDescriptor HandleRegistration(IUserDataDescriptor newDescriptor, IUserDataDescriptor oldDescriptor)
            {
                if (newDescriptor == null) return null;
                if (oldDescriptor != null) return oldDescriptor;
                if (newDescriptor is StandardUserDataDescriptor d) Trim(d);
                return newDescriptor;
            }
        }

        /// <summary>A script may hold objects of this type and call its public members.</summary>
        public static bool Allowed(Type t)
        {
            if (t == null || t.IsPointer || t.IsByRef || t.IsGenericParameter) return false;
            if (t.IsArray) return false;   // (arrays reach Lua as tables)
            if (t.IsGenericType)
            {
                var def = t.GetGenericTypeDefinition();
                if (Collections.Contains(def)) return t.GetGenericArguments().All(a => Simple(a) || Allowed(a));
                if (t.IsGenericTypeDefinition) return false;
            }
            if (t.IsNested) return t.IsNestedPublic && Allowed(t.DeclaringType) && !DeniedName(t);
            if (!t.IsVisible) return false;
            return !DeniedName(t) && (IsGame(t) || IsUnity(t));
        }

        static bool Simple(Type t) => t.IsPrimitive || t == typeof(string) || t == typeof(object) || t == typeof(decimal) || (t.IsEnum && (IsGame(t) || IsUnity(t)));

        static bool IsGame(Type t) => GameAssemblies.Contains(t.Assembly.GetName().Name);

        static bool IsUnity(Type t)
        {
            if (t.Namespace == null || !UnityNamespaces.Contains(t.Namespace)) return false;
            string a = t.Assembly.GetName().Name;
            return a.StartsWith("UnityEngine", StringComparison.Ordinal) || a.StartsWith("Unity.", StringComparison.Ordinal);
        }

        static bool DeniedName(Type t)
        {
            string n = t.Name;
            if (IsGame(t)) return GameDenied.Contains(n) || GameDeniedPrefixes.Any(p => n.StartsWith(p, StringComparison.Ordinal));
            if (t.Namespace == "UnityEngine") return UnityDenied.Contains(n) || UnityDeniedPrefixes.Any(p => n.StartsWith(p, StringComparison.Ordinal));
            return false;
        }

        /// <summary>The members scripts don't get: a game class's public methods and constructors that take a path-like text.</summary>
        static void Trim(StandardUserDataDescriptor d)
        {
            var t = d.Type;
            if (t == null || !IsGame(t)) return;
            const BindingFlags F = BindingFlags.Public | BindingFlags.Instance | BindingFlags.Static | BindingFlags.FlattenHierarchy;
            foreach (var m in t.GetMethods(F))
                if (TakesPath(m) && d.HasMember(m.Name)) d.RemoveMember(m.Name);
            if (t.GetConstructors().Any(TakesPath) && d.HasMember("__new")) d.RemoveMember("__new");
        }

        static bool TakesPath(MethodBase m)
        {
            foreach (var p in m.GetParameters())
            {
                if (p.ParameterType != typeof(string) && p.ParameterType != typeof(string[])) continue;
                foreach (string w in Words(p.Name)) if (PathWords.Contains(w)) return true;
            }
            return false;
        }

        /// <summary>"saveFolderPath" -> save, Folder, Path.</summary>
        static IEnumerable<string> Words(string name)
        {
            if (string.IsNullOrEmpty(name)) yield break;
            int start = 0;
            for (int i = 1; i <= name.Length; i++)
                if (i == name.Length || char.IsUpper(name[i]) || name[i] == '_')
                {
                    if (i > start) yield return name.Substring(start, i - start).Trim('_');
                    start = i;
                }
        }

        // ---- names -> types (game.type, import) ----------------------------------------------------------------------

        static Dictionary<string, Type> byFullName;
        static Dictionary<string, List<Type>> byShortName;

        static void BuildIndex()
        {
            byFullName = new Dictionary<string, Type>(StringComparer.Ordinal);
            byShortName = new Dictionary<string, List<Type>>(StringComparer.Ordinal);
            foreach (var asm in AppDomain.CurrentDomain.GetAssemblies())
            {
                string n = asm.GetName().Name;
                if (!GameAssemblies.Contains(n) && !n.StartsWith("UnityEngine", StringComparison.Ordinal) && !n.StartsWith("Unity.", StringComparison.Ordinal)) continue;
                Type[] types;
                try { types = asm.GetExportedTypes(); } catch (Exception) { continue; }
                foreach (var t in types)
                {
                    if (t.IsGenericTypeDefinition || t.FullName == null || !Allowed(t)) continue;
                    byFullName[t.FullName.Replace('+', '.')] = t;
                    if (!byShortName.TryGetValue(t.Name, out var l)) byShortName[t.Name] = l = new List<Type>();
                    l.Add(t);
                }
            }
        }

        /// <summary>A class by its full name ("GoF2Remake.Data.Session", "UnityEngine.Vector3") or, when only one allowed class
        /// has it, its short name; null with the reason otherwise.</summary>
        public static Type Find(string name, out string error)
        {
            error = null;
            if (byFullName == null) BuildIndex();
            name = (name ?? "").Trim();
            if (byFullName.TryGetValue(name, out var t)) return t;
            if (byShortName.TryGetValue(name, out var l))
            {
                if (l.Count == 1) return l[0];
                // The game's own class wins over Unity's of the same name; two of the game's need the full name.
                var own = l.Where(IsGame).ToList();
                if (own.Count == 1) return own[0];
                error = $"\"{name}\" is the name of several classes: {string.Join(", ", l.Select(x => x.FullName))}";
                return null;
            }
            error = $"no class \"{name}\" scripts can use (the game's classes and Unity's scene classes; not .NET's)";
            return null;
        }

        // ---- what a class offers (game.describe, game.classes, the generated scripting reference) --------------------------

        /// <summary>Every class scripts can name, by full name (the game's first).</summary>
        public static List<Type> AllowedTypes(bool gameOnly = false)
        {
            if (byFullName == null) BuildIndex();
            return byFullName.Values.Where(t => !gameOnly || IsGame(t))
                .OrderBy(t => IsGame(t) ? 0 : 1).ThenBy(t => t.FullName, StringComparer.Ordinal).ToList();
        }

        /// <summary>A type as scripts read it: int, float, string, List&lt;ItemData&gt;, ItemData[], Action&lt;int&gt;.</summary>
        public static string TypeName(Type t)
        {
            if (t == null) return "?";
            if (t.IsByRef) return TypeName(t.GetElementType());
            if (t.IsArray) return TypeName(t.GetElementType()) + "[]";
            if (t == typeof(void)) return "nothing";
            if (t == typeof(int)) return "int";
            if (t == typeof(long)) return "long";
            if (t == typeof(float)) return "float";
            if (t == typeof(double)) return "double";
            if (t == typeof(bool)) return "bool";
            if (t == typeof(string)) return "string";
            if (t == typeof(object)) return "any";
            if (t.IsGenericType)
            {
                string name = t.Name;
                int tick = name.IndexOf('`');
                if (tick > 0) name = name.Substring(0, tick);
                if (t.GetGenericTypeDefinition() == typeof(Nullable<>)) return TypeName(t.GetGenericArguments()[0]) + "?";
                return name + "<" + string.Join(", ", t.GetGenericArguments().Select(TypeName)) + ">";
            }
            return t.IsNested && !t.IsGenericParameter ? TypeName(t.DeclaringType) + "." + t.Name : t.Name;
        }

        /// <summary>A value of this type can pass between the game and Lua (a member with another type is of no use to scripts).</summary>
        public static bool Usable(Type t)
        {
            if (t == null) return false;
            if (t.IsByRef) t = t.GetElementType();
            if (t == typeof(void) || t.IsPrimitive || t == typeof(string) || t == typeof(object) || t == typeof(decimal)) return true;
            if (t.IsArray) return Usable(t.GetElementType());
            if (t.IsEnum) return IsGame(t) || IsUnity(t);
            if (typeof(Delegate).IsAssignableFrom(t))
            {
                var invoke = t.GetMethod("Invoke");
                return invoke != null && Usable(invoke.ReturnType) && invoke.GetParameters().All(p => Usable(p.ParameterType));
            }
            if (t.IsGenericType && !t.IsGenericTypeDefinition)
            {
                var def = t.GetGenericTypeDefinition();
                if (def == typeof(IEnumerable<>) || def == typeof(IReadOnlyList<>) || def == typeof(IList<>) || def == typeof(ICollection<>)
                    || def == typeof(IReadOnlyCollection<>) || def == typeof(IDictionary<,>) || def == typeof(IReadOnlyDictionary<,>))
                    return t.GetGenericArguments().All(Usable);
            }
            return Allowed(t);
        }

        /// <summary>The public members of a class that scripts can use, one a line ("static Credits: long", "Item(index: int):
        /// ItemData", "event Changed()"): its own, without what it inherits (the last line names its base class).</summary>
        /// <param name="all">The members that aren't public too ("private ...": game.field / game.setField / game.call reach them).</param>
        public static List<string> Describe(Type t, bool all = false)
        {
            var lines = new List<string>();
            if (t == null || !Allowed(t)) return lines;
            BindingFlags F = BindingFlags.Public | BindingFlags.Instance | BindingFlags.Static | BindingFlags.DeclaredOnly | (all ? BindingFlags.NonPublic : 0);
            if (t.IsEnum)
            {
                lines.Add("values: " + string.Join(", ", Enum.GetNames(t).Select(n => n + " = " + Convert.ToInt64(Enum.Parse(t, n)))));
                return lines;
            }
            string St(bool isStatic) => isStatic ? "static " : "";
            string Pr(bool isPublic) => isPublic ? "" : "private ";
            foreach (var f in t.GetFields(F).OrderBy(f => f.Name, StringComparer.Ordinal))
            {
                if (f.IsSpecialName || !Usable(f.FieldType) || f.Name.IndexOf('<') >= 0) continue;   // (not a property's hidden field)
                lines.Add(Pr(f.IsPublic) + St(f.IsStatic) + f.Name + ": " + TypeName(f.FieldType) + (f.IsLiteral ? " = " + f.GetRawConstantValue() : f.IsInitOnly ? " (read only)" : ""));
            }
            foreach (var p in t.GetProperties(F).OrderBy(p => p.Name, StringComparer.Ordinal))
            {
                if (p.GetIndexParameters().Length > 0 || !Usable(p.PropertyType)) continue;
                var get = p.GetGetMethod(all);
                var set = p.GetSetMethod(all);
                if (get == null && set == null) continue;
                lines.Add(Pr((get ?? set).IsPublic) + St((get ?? set).IsStatic) + p.Name + ": " + TypeName(p.PropertyType) + (set == null ? " (read only)" : ""));
            }
            foreach (var c in t.GetConstructors())
            {
                if (TakesPathOf(t, c) || !c.GetParameters().All(p => Usable(p.ParameterType))) continue;
                lines.Add("__new(" + Parameters(c) + ")");
            }
            foreach (var m in t.GetMethods(F).OrderBy(m => m.Name, StringComparer.Ordinal))
            {
                if (m.IsSpecialName || m.IsGenericMethodDefinition || TakesPathOf(t, m) || m.Name.IndexOf('<') >= 0) continue;
                if (!Usable(m.ReturnType) || !m.GetParameters().All(p => Usable(p.ParameterType))) continue;
                if (m.Name == "Equals" || m.Name == "GetHashCode" || m.Name == "ToString") continue;
                lines.Add(Pr(m.IsPublic) + St(m.IsStatic) + m.Name + "(" + Parameters(m) + ")" + (m.ReturnType == typeof(void) ? "" : ": " + TypeName(m.ReturnType)));
            }
            foreach (var e in t.GetEvents(F).OrderBy(e => e.Name, StringComparer.Ordinal))
            {
                var invoke = e.EventHandlerType?.GetMethod("Invoke");
                if (invoke == null || !Usable(e.EventHandlerType)) continue;
                lines.Add(St(e.GetAddMethod()?.IsStatic ?? false) + "event " + e.Name + "(" + Parameters(invoke) + ")");
            }
            if (t.BaseType != null && t.BaseType != typeof(object) && t.BaseType != typeof(ValueType) && Allowed(t.BaseType))
                lines.Add("and what " + TypeName(t.BaseType) + " has");
            return lines;
        }

        static bool TakesPathOf(Type owner, MethodBase m) => IsGame(owner) && TakesPath(m);

        static string Parameters(MethodBase m) =>
            string.Join(", ", m.GetParameters().Select(p => (p.IsOut ? "out " : "") + p.Name + ": " + TypeName(p.ParameterType) + (p.HasDefaultValue ? "?" : "")));

        // ---- Lua functions as C# delegates (game.hook, game.listen, Action parameters) ----------------------------------

        /// <summary>A delegate of the given type that calls the Lua function with its arguments and gives its result back
        /// (nothing returned for a value type: its default). Built with an expression tree, which IL2CPP interprets.</summary>
        public static Delegate MakeDelegate(Type delegateType, DynValue fn) => MakeDelegate(delegateType, args => ModScripts.CallFromDelegate(fn, args));

        /// <summary>'body' gets the delegate's arguments; what it returns (a Lua value, or the C# value itself) is the result.</summary>
        public static Delegate MakeDelegate(Type delegateType, Func<object[], object> body)
        {
            var invoke = delegateType.GetMethod("Invoke");
            if (invoke == null) throw new ScriptRuntimeException($"{delegateType.Name} is not a delegate");
            var ps = invoke.GetParameters();
            var pe = ps.Select(p => Expression.Parameter(p.ParameterType, p.Name)).ToArray();
            // The body gets every argument (an out parameter as null); what it returns is the result, and after it the
            // values of the out / ref parameters in order (CoerceAt).
            Expression array = Expression.NewArrayInit(typeof(object),
                ps.Select((p, i) => p.IsOut ? (Expression)Expression.Constant(null, typeof(object)) : Expression.Convert(pe[i], typeof(object))));
            var at = typeof(ScriptAccess).GetMethod(nameof(CoerceAt), BindingFlags.Public | BindingFlags.Static);
            var result = Expression.Variable(typeof(object), "result");
            var steps = new List<Expression> { Expression.Assign(result, Expression.Invoke(Expression.Constant(body), array)) };
            int extra = 0;
            for (int i = 0; i < ps.Length; i++)
            {
                if (!ps[i].ParameterType.IsByRef) continue;
                var element = ps[i].ParameterType.GetElementType();
                steps.Add(Expression.Assign(pe[i], Expression.Convert(Expression.Call(at, result, Expression.Constant(++extra), Expression.Constant(element, typeof(Type))), element)));
            }
            if (invoke.ReturnType == typeof(void)) steps.Add(Expression.Empty());
            else steps.Add(Expression.Convert(Expression.Call(at, result, Expression.Constant(0), Expression.Constant(invoke.ReturnType, typeof(Type))), invoke.ReturnType));
            return Expression.Lambda(delegateType, Expression.Block(invoke.ReturnType, new[] { result }, steps), pe).Compile();
        }

        /// <summary>A delegate body's result as the delegate's return type.</summary>
        public static object Coerce(object result, Type type)
        {
            if (result is DynValue v) return FromLua(v, type);
            if (result == null || !type.IsInstanceOfType(result)) return type.IsValueType ? Activator.CreateInstance(type) : null;
            return result;
        }

        /// <summary>A Lua value as the C# type a delegate returns or a method takes (nil for a value type: its default).</summary>
        public static object FromLua(DynValue v, Type type)
        {
            if (v == null || v.IsNil() || v.IsVoid()) return type.IsValueType ? Activator.CreateInstance(type) : null;
            if (type == typeof(bool)) return v.CastToBool();
            return v.ToObject(type);
        }
    }
}
