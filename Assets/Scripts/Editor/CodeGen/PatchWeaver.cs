// PatchWeaver.cs
// The build step behind the mod scripts' game.patch (Scripts/Runtime/Core/PatchRuntime.cs has the whole picture): an
// ILPostProcessor, so Unity runs it on the game's five runtime assemblies after every compile, the Editor's and a
// player's, before IL2CPP. Every method it can handle gets [PatchSite(id)] and, in front of its own code:
//     PatchHandler h = PatchTable.Table[id];
//     if (h != null && !PatchRuntime.Skip(h))
//     {
//         object r = h(this or null, new object[] { each argument, boxed; a ref / out one's value });
//         each ref / out argument = the array's value;
//         return (T)r;
//     }
// PatchTable (Table, the Skipped count) is made here, one per assembly. An overridable method also gets
// "__patchBase_<Name>": the same signature, calling it without virtual dispatch (PatchRuntime's way to its own code).
// Left out (Eligible): what can't be boxed or isn't one method one body, and what must stay fast or is another tool's:
// [AggressiveInlining], RPCs and Netcode's generated members ("__..."), Burst, native callbacks.
// A failure leaves the assembly as compiled (a warning in the Console): the game runs, patches on it don't.

using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using Mono.Cecil;
using Mono.Cecil.Cil;
using Mono.Cecil.Rocks;
using Unity.CompilationPipeline.Common.Diagnostics;
using Unity.CompilationPipeline.Common.ILPostProcessing;

namespace GoF2Remake.Patching.CodeGen
{
    class PatchWeaver : ILPostProcessor
    {
        static readonly HashSet<string> Assemblies = new HashSet<string> { "GoF2.Core", "GoF2.Game", "GoF2.Space", "GoF2.Net", "GoF2.App" };
        const string Core = "GoF2.Core", Ns = "GoF2Remake.Patching", BasePrefix = "__patchBase_";

        public override ILPostProcessor GetInstance() => this;

        public override bool WillProcess(ICompiledAssembly compiledAssembly) => Assemblies.Contains(compiledAssembly.Name);

        public override ILPostProcessResult Process(ICompiledAssembly compiledAssembly)
        {
            var diagnostics = new List<DiagnosticMessage>();
            try { return new ILPostProcessResult(new Weave(compiledAssembly).Run(), diagnostics); }
            catch (Exception e)
            {
                diagnostics.Add(new DiagnosticMessage
                {
                    DiagnosticType = DiagnosticType.Warning,
                    MessageData = $"PatchWeaver: {compiledAssembly.Name} was not made patchable (game.patch won't work on its classes): {e}",
                });
                return new ILPostProcessResult(null, diagnostics);
            }
        }

        sealed class Weave
        {
            readonly ICompiledAssembly compiled;
            readonly Dictionary<string, bool> refLike = new Dictionary<string, bool>();
            ModuleDefinition module;
            TypeReference handlerType, objectArray;
            MethodReference invoke, skip, siteCtor;
            FieldDefinition table;
            int skipped;

            public Weave(ICompiledAssembly compiled) { this.compiled = compiled; }

            public InMemoryAssembly Run()
            {
                using (var resolver = new Resolver(compiled.References))
                using (var assembly = Read(resolver))
                {
                    module = assembly.MainModule;
                    if (module.GetType(Ns, "PatchTable") != null) return null;   // woven already
                    Bind(resolver);

                    var tableType = new TypeDefinition(Ns, "PatchTable",
                        TypeAttributes.NotPublic | TypeAttributes.Abstract | TypeAttributes.Sealed | TypeAttributes.BeforeFieldInit | TypeAttributes.Class,
                        module.TypeSystem.Object);
                    table = new FieldDefinition("Table", FieldAttributes.Public | FieldAttributes.Static | FieldAttributes.InitOnly, new ArrayType(handlerType));
                    tableType.Fields.Add(table);

                    int count = 0;
                    foreach (var type in module.GetTypes().ToList())
                    {
                        if (!EligibleType(type)) { skipped += type.Methods.Count(m => m.HasBody); continue; }
                        foreach (var m in type.Methods.ToList())
                        {
                            if (!m.HasBody) continue;
                            if (!Eligible(m)) { skipped++; continue; }
                            Prologue(m, count);
                            var site = new CustomAttribute(siteCtor);
                            site.ConstructorArguments.Add(new CustomAttributeArgument(module.TypeSystem.Int32, count));
                            m.CustomAttributes.Add(site);
                            if (m.IsVirtual && !m.IsFinal && !type.IsSealed) type.Methods.Add(BaseCall(m));
                            count++;
                        }
                    }

                    tableType.Fields.Add(new FieldDefinition("Skipped", FieldAttributes.Public | FieldAttributes.Static | FieldAttributes.Literal | FieldAttributes.HasDefault,
                                                             module.TypeSystem.Int32) { Constant = skipped });
                    var cctor = new MethodDefinition(".cctor",
                        MethodAttributes.Private | MethodAttributes.HideBySig | MethodAttributes.SpecialName | MethodAttributes.RTSpecialName | MethodAttributes.Static,
                        module.TypeSystem.Void);
                    var il = cctor.Body.GetILProcessor();
                    il.Emit(OpCodes.Ldc_I4, count);
                    il.Emit(OpCodes.Newarr, handlerType);
                    il.Emit(OpCodes.Stsfld, table);
                    il.Emit(OpCodes.Ret);
                    tableType.Methods.Add(cctor);
                    module.Types.Add(tableType);
                    return Write(assembly);
                }
            }

            /// <summary>The runtime's members as this module names them: its own definitions in GoF2.Core, references elsewhere.</summary>
            void Bind(Resolver resolver)
            {
                objectArray = new ArrayType(module.TypeSystem.Object);
                if (compiled.Name == Core)
                {
                    var handler = module.GetType(Ns, "PatchHandler") ?? throw new InvalidOperationException("GoF2.Core has no PatchHandler");
                    var runtime = module.GetType(Ns, "PatchRuntime") ?? throw new InvalidOperationException("GoF2.Core has no PatchRuntime");
                    var site = module.GetType(Ns, "PatchSiteAttribute") ?? throw new InvalidOperationException("GoF2.Core has no PatchSiteAttribute");
                    handlerType = handler;
                    invoke = handler.Methods.First(m => m.Name == "Invoke");
                    skip = runtime.Methods.First(m => m.Name == "Skip" && m.IsStatic);
                    siteCtor = site.Methods.First(m => m.IsConstructor && !m.IsStatic && m.Parameters.Count == 1);
                    return;
                }
                var scope = module.AssemblyReferences.FirstOrDefault(a => a.Name == Core);
                if (scope == null)
                {
                    // (the compiler leaves out a reference no type is used from)
                    var core = resolver.Resolve(new AssemblyNameReference(Core, new Version(0, 0, 0, 0))) ?? throw new InvalidOperationException("GoF2.Core isn't among the references");
                    scope = AssemblyNameReference.Parse(core.Name.FullName);
                    module.AssemblyReferences.Add(scope);
                }
                handlerType = new TypeReference(Ns, "PatchHandler", module, scope);
                var runtimeType = new TypeReference(Ns, "PatchRuntime", module, scope);
                var siteType = new TypeReference(Ns, "PatchSiteAttribute", module, scope);
                invoke = new MethodReference("Invoke", module.TypeSystem.Object, handlerType) { HasThis = true };
                invoke.Parameters.Add(new ParameterDefinition(module.TypeSystem.Object));
                invoke.Parameters.Add(new ParameterDefinition(objectArray));
                skip = new MethodReference("Skip", module.TypeSystem.Boolean, runtimeType);
                skip.Parameters.Add(new ParameterDefinition(handlerType));
                siteCtor = new MethodReference(".ctor", module.TypeSystem.Void, siteType) { HasThis = true };
                siteCtor.Parameters.Add(new ParameterDefinition(module.TypeSystem.Int32));
            }

            // ---- what is woven ------------------------------------------------------------------------------------------

            static bool Has(ICustomAttributeProvider p, string attribute) =>
                p.HasCustomAttributes && p.CustomAttributes.Any(a => a.AttributeType.Name == attribute);

            static bool EligibleType(TypeDefinition type)
            {
                if (type.IsInterface) return false;
                for (var t = type; t != null; t = t.DeclaringType)
                {
                    if (t.HasGenericParameters || t.Name.IndexOf('<') >= 0) return false;
                    if (Has(t, "CompilerGeneratedAttribute") || Has(t, "BurstCompileAttribute")) return false;
                    if (t.Namespace == Ns || t.Namespace.StartsWith("__", StringComparison.Ordinal)) return false;   // the runtime itself; Netcode's __GEN
                }
                return true;
            }

            bool Eligible(MethodDefinition m)
            {
                if (m.IsAbstract || m.IsConstructor || m.IsPInvokeImpl || m.IsRuntime || m.IsInternalCall || m.IsNative || m.IsUnmanaged) return false;
                if (m.Body.Instructions.Count == 0 || m.HasGenericParameters || m.CallingConvention == MethodCallingConvention.VarArg) return false;
                if (!m.IsStatic && m.DeclaringType.IsValueType) return false;   // (its "this" is a pointer: a boxed copy would lose what it changes)
                if (m.Name.IndexOf('<') >= 0 || m.Name == "Finalize" || m.Name.StartsWith("__", StringComparison.Ordinal)) return false;
                if (m.AggressiveInlining) return false;
                if (m.HasCustomAttributes)
                    foreach (var a in m.CustomAttributes)
                    {
                        string n = a.AttributeType.Name;
                        if (n == "CompilerGeneratedAttribute" || n == "BurstCompileAttribute" || n == "MonoPInvokeCallbackAttribute"
                            || n == "UnmanagedCallersOnlyAttribute" || n.EndsWith("RpcAttribute", StringComparison.Ordinal)) return false;
                    }
                var ret = Strip(m.ReturnType);
                if (ret.MetadataType != MetadataType.Void && !Boxable(ret)) return false;
                foreach (var p in m.Parameters)
                {
                    var t = Strip(p.ParameterType);
                    if (t is ByReferenceType r) t = Strip(r.ElementType);
                    if (!Boxable(t)) return false;
                }
                return true;
            }

            static TypeReference Strip(TypeReference t)
            {
                while (t is IModifierType m) t = m.ElementType;
                return t;
            }

            /// <summary>A value of the type can be held as an object (no pointer, reference or ref struct).</summary>
            bool Boxable(TypeReference t)
            {
                t = Strip(t);
                if (t is PointerType || t is FunctionPointerType || t is ByReferenceType || t is PinnedType || t is SentinelType || t.IsGenericParameter) return false;
                if (t.MetadataType == MetadataType.Void || t.MetadataType == MetadataType.TypedByReference) return false;
                if (t is ArrayType) return true;
                if (t.FullName == "System.ArgIterator" || t.FullName == "System.RuntimeArgumentHandle") return false;
                if (t.IsPrimitive || !t.IsValueType) return true;
                return !RefLike(t is GenericInstanceType g ? g.ElementType : t);
            }

            /// <summary>A ref struct (Span and the like), or a struct that can't be looked up (left out to be safe).</summary>
            bool RefLike(TypeReference t)
            {
                if (refLike.TryGetValue(t.FullName, out bool known)) return known;
                bool like = true;
                try
                {
                    var def = t.Resolve();
                    if (def != null) like = def.HasCustomAttributes && def.CustomAttributes.Any(a => a.AttributeType.FullName == "System.Runtime.CompilerServices.IsByRefLikeAttribute");
                }
                catch (Exception) { }
                return refLike[t.FullName] = like;
            }

            // ---- the woven code -----------------------------------------------------------------------------------------

            void Prologue(MethodDefinition m, int id)
            {
                var body = m.Body;
                body.SimplifyMacros();   // (long branches and plain ldarg, so the insertion can't overflow a short jump)
                var first = body.Instructions[0];
                var handler = new VariableDefinition(handlerType);
                body.Variables.Add(handler);
                var ps = m.Parameters;
                bool anyRef = ps.Any(p => Strip(p.ParameterType) is ByReferenceType);
                VariableDefinition args = null;
                if (anyRef) body.Variables.Add(args = new VariableDefinition(objectArray));

                var code = new List<Instruction>
                {
                    Instruction.Create(OpCodes.Ldsfld, table),
                    Instruction.Create(OpCodes.Ldc_I4, id),
                    Instruction.Create(OpCodes.Ldelem_Ref),
                    Instruction.Create(OpCodes.Stloc, handler),
                    Instruction.Create(OpCodes.Ldloc, handler),
                    Instruction.Create(OpCodes.Brfalse, first),
                    Instruction.Create(OpCodes.Ldloc, handler),
                    Instruction.Create(OpCodes.Call, skip),
                    Instruction.Create(OpCodes.Brtrue, first),
                    Instruction.Create(OpCodes.Ldloc, handler),
                    m.IsStatic ? Instruction.Create(OpCodes.Ldnull) : Instruction.Create(OpCodes.Ldarg, body.ThisParameter),
                    Instruction.Create(OpCodes.Ldc_I4, ps.Count),
                    Instruction.Create(OpCodes.Newarr, module.TypeSystem.Object),
                };
                for (int i = 0; i < ps.Count; i++)
                {
                    var t = Strip(ps[i].ParameterType);
                    code.Add(Instruction.Create(OpCodes.Dup));
                    code.Add(Instruction.Create(OpCodes.Ldc_I4, i));
                    code.Add(Instruction.Create(OpCodes.Ldarg, ps[i]));
                    if (t is ByReferenceType r)
                    {
                        t = Strip(r.ElementType);
                        code.Add(Instruction.Create(OpCodes.Ldobj, t));
                    }
                    if (t.IsValueType) code.Add(Instruction.Create(OpCodes.Box, t));
                    code.Add(Instruction.Create(OpCodes.Stelem_Ref));
                }
                if (anyRef)
                {
                    code.Add(Instruction.Create(OpCodes.Dup));
                    code.Add(Instruction.Create(OpCodes.Stloc, args));
                }
                code.Add(Instruction.Create(OpCodes.Callvirt, invoke));
                for (int i = 0; anyRef && i < ps.Count; i++)
                {
                    if (!(Strip(ps[i].ParameterType) is ByReferenceType r) || (ps[i].IsIn && !ps[i].IsOut)) continue;   // (an "in" value isn't written back)
                    var t = Strip(r.ElementType);
                    code.Add(Instruction.Create(OpCodes.Ldarg, ps[i]));
                    code.Add(Instruction.Create(OpCodes.Ldloc, args));
                    code.Add(Instruction.Create(OpCodes.Ldc_I4, i));
                    code.Add(Instruction.Create(OpCodes.Ldelem_Ref));
                    code.Add(Instruction.Create(OpCodes.Unbox_Any, t));
                    code.Add(Instruction.Create(OpCodes.Stobj, t));
                }
                var ret = Strip(m.ReturnType);
                code.Add(ret.MetadataType == MetadataType.Void ? Instruction.Create(OpCodes.Pop) : Instruction.Create(OpCodes.Unbox_Any, ret));
                code.Add(Instruction.Create(OpCodes.Ret));

                var il = body.GetILProcessor();
                foreach (var instruction in code) il.InsertBefore(first, instruction);
                body.OptimizeMacros();
            }

            /// <summary>A private method calling the overridable 'm' itself, whatever the object's class overrides.</summary>
            MethodDefinition BaseCall(MethodDefinition m)
            {
                var call = new MethodDefinition(BasePrefix + m.Name, MethodAttributes.Private | MethodAttributes.HideBySig, m.ReturnType) { HasThis = true };
                foreach (var p in m.Parameters)
                    call.Parameters.Add(new ParameterDefinition(p.Name, p.Attributes & ~(ParameterAttributes.Optional | ParameterAttributes.HasDefault | ParameterAttributes.HasFieldMarshal), p.ParameterType));
                var il = call.Body.GetILProcessor();
                il.Emit(OpCodes.Ldarg_0);
                foreach (var p in call.Parameters) il.Emit(OpCodes.Ldarg, p);
                il.Emit(OpCodes.Call, m);
                il.Emit(OpCodes.Ret);
                return call;
            }

            // ---- reading and writing ------------------------------------------------------------------------------------

            AssemblyDefinition Read(IAssemblyResolver resolver)
            {
                var pdb = compiled.InMemoryAssembly.PdbData;
                bool symbols = pdb != null && pdb.Length > 0;
                return AssemblyDefinition.ReadAssembly(new MemoryStream(compiled.InMemoryAssembly.PeData), new ReaderParameters
                {
                    AssemblyResolver = resolver,
                    ReadingMode = ReadingMode.Immediate,
                    ReadWrite = true,
                    ReadSymbols = symbols,
                    SymbolReaderProvider = symbols ? new PortablePdbReaderProvider() : null,
                    SymbolStream = symbols ? new MemoryStream(pdb) : null,
                });
            }

            static InMemoryAssembly Write(AssemblyDefinition assembly)
            {
                var pe = new MemoryStream();
                var pdb = new MemoryStream();
                bool symbols = assembly.MainModule.HasSymbols;
                assembly.Write(pe, new WriterParameters
                {
                    SymbolWriterProvider = symbols ? new PortablePdbWriterProvider() : null,
                    WriteSymbols = symbols,
                    SymbolStream = symbols ? pdb : null,
                });
                return new InMemoryAssembly(pe.ToArray(), symbols ? pdb.ToArray() : null);
            }
        }

        /// <summary>The compilation's own references by assembly name (Cecil's default looks in folders the build doesn't use).</summary>
        sealed class Resolver : IAssemblyResolver
        {
            readonly Dictionary<string, string> paths = new Dictionary<string, string>();
            readonly Dictionary<string, AssemblyDefinition> cache = new Dictionary<string, AssemblyDefinition>();

            public Resolver(string[] references)
            {
                foreach (string r in references) paths[Path.GetFileNameWithoutExtension(r)] = r;
            }

            public AssemblyDefinition Resolve(AssemblyNameReference name) => Resolve(name, new ReaderParameters(ReadingMode.Deferred));

            public AssemblyDefinition Resolve(AssemblyNameReference name, ReaderParameters parameters)
            {
                if (cache.TryGetValue(name.Name, out var cached)) return cached;
                if (!paths.TryGetValue(name.Name, out string path) || !File.Exists(path)) return cache[name.Name] = null;
                parameters.AssemblyResolver = this;
                // (in memory: the file may be rewritten while this compile still reads it)
                return cache[name.Name] = AssemblyDefinition.ReadAssembly(new MemoryStream(File.ReadAllBytes(path)), parameters);
            }

            public void Dispose()
            {
                foreach (var a in cache.Values) a?.Dispose();
                cache.Clear();
            }
        }
    }
}
