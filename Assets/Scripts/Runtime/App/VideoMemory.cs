// VideoMemory.cs
// Remake (2026-11-20, the benchmark's VRAM; docs/notes/options.md "Benchmark"): the video memory this process has in
// use, as Windows counts it (DXGI: IDXGIAdapter3::QueryVideoMemoryInfo, the adapter's local segment: what the Task
// Manager shows as the process's dedicated GPU memory, an upscaler's and frame generation's buffers included). Unity's
// own counter ("Gfx Used Memory") reads 0 in a release build. The process only has memory on the adapter it renders
// with, so the adapter with the most in use is the one.
// shortcut: Windows only (0 elsewhere: the benchmark then shows Unity's counter or "not measured"); other platforms
// need their own query when someone asks for the number there.

using System;
using System.Runtime.InteropServices;

namespace GoF2Remake
{
    [Unity.Scripting.LifecycleManagement.NoAutoStaticsCleanup]
    public static class VideoMemory
    {
#if UNITY_STANDALONE_WIN || UNITY_EDITOR_WIN
        [StructLayout(LayoutKind.Sequential)]
        struct Info { public ulong budget, currentUsage, availableForReservation, currentReservation; }

        [DllImport("dxgi")] static extern int CreateDXGIFactory1(ref Guid iid, out IntPtr factory);

        // COM calls through the objects' vtables (slots: IUnknown 0-2, IDXGIObject 3-6, then the interface's own).
        [UnmanagedFunctionPointer(CallingConvention.StdCall)] delegate int QueryInterfaceFn(IntPtr self, ref Guid iid, out IntPtr result);
        [UnmanagedFunctionPointer(CallingConvention.StdCall)] delegate uint ReleaseFn(IntPtr self);
        [UnmanagedFunctionPointer(CallingConvention.StdCall)] delegate int EnumAdapters1Fn(IntPtr self, uint index, out IntPtr adapter);
        [UnmanagedFunctionPointer(CallingConvention.StdCall)] delegate int QueryVideoMemoryInfoFn(IntPtr self, uint node, int segmentGroup, out Info info);
        const int QueryInterfaceSlot = 0, ReleaseSlot = 2, EnumAdapters1Slot = 12, QueryVideoMemoryInfoSlot = 14;

        static T Fn<T>(IntPtr obj, int slot) where T : Delegate =>
            Marshal.GetDelegateForFunctionPointer<T>(Marshal.ReadIntPtr(Marshal.ReadIntPtr(obj), slot * IntPtr.Size));

        static void Release(IntPtr obj) { if (obj != IntPtr.Zero) Fn<ReleaseFn>(obj, ReleaseSlot)(obj); }

        static bool looked;
        static IntPtr adapter;   // the IDXGIAdapter3 this process renders with, kept for the process's life
        static QueryVideoMemoryInfoFn query;

        static ulong Usage(IntPtr adapter3)
        {
            var fn = Fn<QueryVideoMemoryInfoFn>(adapter3, QueryVideoMemoryInfoSlot);
            return fn(adapter3, 0, 0, out Info info) >= 0 ? info.currentUsage : 0ul;   // (node 0, DXGI_MEMORY_SEGMENT_GROUP_LOCAL)
        }

        static void Find()
        {
            looked = true;
            IntPtr factory = IntPtr.Zero;
            try
            {
                var factoryId = new Guid("770aae78-f26f-4dba-a829-253c83d1b387");    // IDXGIFactory1
                var adapter3Id = new Guid("645967a4-1392-4310-a798-8053ce3e93fd");   // IDXGIAdapter3
                if (CreateDXGIFactory1(ref factoryId, out factory) < 0 || factory == IntPtr.Zero) return;
                var enumerate = Fn<EnumAdapters1Fn>(factory, EnumAdapters1Slot);
                ulong most = 0;
                for (uint i = 0; i < 16 && enumerate(factory, i, out IntPtr a) >= 0 && a != IntPtr.Zero; i++)
                {
                    int hr = Fn<QueryInterfaceFn>(a, QueryInterfaceSlot)(a, ref adapter3Id, out IntPtr a3);
                    Release(a);
                    if (hr < 0 || a3 == IntPtr.Zero) continue;
                    ulong used = Usage(a3);
                    if (used > most) { most = used; Release(adapter); adapter = a3; } else Release(a3);
                }
                if (adapter != IntPtr.Zero) query = Fn<QueryVideoMemoryInfoFn>(adapter, QueryVideoMemoryInfoSlot);
            }
            catch (Exception e) { UnityEngine.Debug.LogWarning("Video memory: not read: " + e.Message); adapter = IntPtr.Zero; query = null; }
            finally { Release(factory); }
        }

        /// <summary>The video memory this process has in use, MB; 0 when it can't be read.</summary>
        public static float UsedMb()
        {
            if (!looked) Find();
            if (query == null) return 0f;
            return query(adapter, 0, 0, out Info info) >= 0 ? info.currentUsage / 1048576f : 0f;
        }
#else
        /// <summary>The video memory this process has in use, MB; 0 when it can't be read (not on this platform).</summary>
        public static float UsedMb() => 0f;
#endif
    }
}
