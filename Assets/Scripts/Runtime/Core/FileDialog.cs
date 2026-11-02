// FileDialog.cs
// Remake-only: the system's save / open file dialog for SaveTransfer (and, on Windows, the mod browser's Import mod). The Editor uses EditorUtility's panels; a Windows
// player calls comdlg32's GetSaveFileNameW / GetOpenFileNameW (modal: the game waits while it is open). Elsewhere there
// is no dialog (Available false) and SaveTransfer uses its Transfer folder. A macOS player has AppKit's open panel for
// ModImport and SaveFolderPicker alone (Open with a title, OpenFolder).

using System;
using System.IO;
using System.Runtime.InteropServices;
using UnityEngine;

namespace GoF2Remake.Data
{
    public static class FileDialog
    {
#if UNITY_EDITOR || UNITY_STANDALONE_WIN
        public static bool Available => true;
#else
        public static bool Available => false;
#endif

        static string StartFolder => Environment.GetFolderPath(Environment.SpecialFolder.MyDocuments);

        /// <summary>Asks where to write a file ('extension' without the dot). Null: cancelled / no dialog.</summary>
        public static string Save(string defaultName, string extension)
        {
#if UNITY_EDITOR
            string p = UnityEditor.EditorUtility.SaveFilePanel("Export saves", StartFolder, Path.GetFileNameWithoutExtension(defaultName), extension);
            return string.IsNullOrEmpty(p) ? null : p;
#elif UNITY_STANDALONE_WIN
            return Win32.Show(true, defaultName, extension);
#else
            return null;
#endif
        }

        /// <summary>Asks for a file to read ('extension' without the dot). Null: cancelled / no dialog.</summary>
        public static string Open(string extension)
        {
#if UNITY_EDITOR
            string p = UnityEditor.EditorUtility.OpenFilePanel("Import saves", StartFolder, extension);
            return string.IsNullOrEmpty(p) ? null : p;
#elif UNITY_STANDALONE_WIN
            return Win32.Show(false, null, extension);
#else
            return null;
#endif
        }

        /// <summary>Asks for a file to read with its own title and filter ('extensions' without the dots; the Mods' Import
        /// mod). Windows and macOS players only (the Editor and the other platforms have their own pickers,
        /// Modding.ModImport). Null: cancelled / no dialog; macOS throws when its panel can't be shown.</summary>
        public static string Open(string title, string filterName, params string[] extensions)
        {
#if UNITY_STANDALONE_WIN && !UNITY_EDITOR
            return Win32.Show(false, null, extensions.Length > 0 ? extensions[0] : "", title, filterName, extensions);
#elif UNITY_STANDALONE_OSX && !UNITY_EDITOR
            return Mac.Show(title, extensions, false);
#else
            return null;
#endif
        }

        /// <summary>Asks for a folder (SaveFolderPicker). macOS player only. Null: cancelled / no dialog; throws when the
        /// panel can't be shown.</summary>
        public static string OpenFolder(string title)
        {
#if UNITY_STANDALONE_OSX && !UNITY_EDITOR
            return Mac.Show(title, null, true);
#else
            return null;
#endif
        }

#if UNITY_STANDALONE_OSX && !UNITY_EDITOR
        /// <summary>AppKit's NSOpenPanel through the Objective-C runtime (libobjc's objc_msgSend, as MacGameControllers reads
        /// GameController: nothing to compile on a Mac), run modal on the main thread, which is AppKit's in a Mac player.
        /// It replaced osascript's choose file on a worker thread, whose pick never reached the game (2026-11-02: the Mod
        /// Builder's .glb picked, then nothing). A file chosen in the game's own panel is also the player's choice to macOS's
        /// privacy checks, so one in Downloads / Documents / Desktop reads without a prompt.</summary>
        static class Mac
        {
            const string LibObjc = "/usr/lib/libobjc.A.dylib";
            const long ModalResponseOk = 1;   // NSModalResponseOK

            [DllImport(LibObjc)] static extern IntPtr objc_getClass(string name);
            [DllImport(LibObjc)] static extern IntPtr sel_registerName(string name);
            [DllImport(LibObjc)] static extern IntPtr objc_autoreleasePoolPush();
            [DllImport(LibObjc)] static extern void objc_autoreleasePoolPop(IntPtr pool);
            // objc_msgSend with each signature it is called with (BOOL arguments as a byte: bool on arm64, signed char on x86_64).
            [DllImport(LibObjc, EntryPoint = "objc_msgSend")] static extern IntPtr Send(IntPtr self, IntPtr sel);
            [DllImport(LibObjc, EntryPoint = "objc_msgSend")] static extern IntPtr Send(IntPtr self, IntPtr sel, IntPtr arg);
            [DllImport(LibObjc, EntryPoint = "objc_msgSend")] static extern void SendFlag(IntPtr self, IntPtr sel, byte flag);
            [DllImport(LibObjc, EntryPoint = "objc_msgSend")] static extern long SendLong(IntPtr self, IntPtr sel);
            [DllImport(LibObjc, EntryPoint = "objc_msgSend")] static extern byte SendBool(IntPtr self, IntPtr sel, IntPtr arg);

            static IntPtr S(string name) => sel_registerName(name);

            static IntPtr NSString(string s)
            {
                IntPtr utf8 = Marshal.StringToCoTaskMemUTF8(s ?? "");
                try { return Send(objc_getClass("NSString"), S("stringWithUTF8String:"), utf8); }
                finally { Marshal.FreeCoTaskMem(utf8); }
            }

            /// <summary>The panel for one file ('extensions' without the dots; none or "*": any file) or one folder; the
            /// chosen path, null when cancelled.</summary>
            public static string Show(string title, string[] extensions, bool folder)
            {
                IntPtr panelClass = objc_getClass("NSOpenPanel");
                if (panelClass == IntPtr.Zero) throw new Exception("no NSOpenPanel");
                IntPtr pool = objc_autoreleasePoolPush();
                try
                {
                    IntPtr panel = Send(panelClass, S("openPanel"));
                    if (panel == IntPtr.Zero) throw new Exception("NSOpenPanel didn't open");
                    SendFlag(panel, S("setCanChooseFiles:"), (byte)(folder ? 0 : 1));
                    SendFlag(panel, S("setCanChooseDirectories:"), (byte)(folder ? 1 : 0));
                    SendFlag(panel, S("setCanCreateDirectories:"), (byte)(folder ? 1 : 0));
                    SendFlag(panel, S("setAllowsMultipleSelection:"), 0);
                    if (!string.IsNullOrEmpty(title)) Send(panel, S("setMessage:"), NSString(title));
                    // setAllowedFileTypes: is deprecated since macOS 12 (allowedContentTypes needs UTTypes) but still filters;
                    // asked first, since an unknown selector raises an Objective-C exception, which C# can't catch.
                    bool filter = !folder && extensions != null && extensions.Length > 0
                                  && Array.TrueForAll(extensions, e => !string.IsNullOrEmpty(e) && e != "*");
                    IntPtr selTypes = S("setAllowedFileTypes:");
                    if (filter && SendBool(panel, S("respondsToSelector:"), selTypes) != 0)
                    {
                        IntPtr types = Send(objc_getClass("NSMutableArray"), S("array"));
                        foreach (var e in extensions) Send(types, S("addObject:"), NSString(e));
                        Send(panel, selTypes, types);
                    }
                    if (SendLong(panel, S("runModal")) != ModalResponseOk) return null;
                    IntPtr url = Send(panel, S("URL"));
                    IntPtr path = url == IntPtr.Zero ? IntPtr.Zero : Send(url, S("path"));
                    IntPtr utf8 = path == IntPtr.Zero ? IntPtr.Zero : Send(path, S("UTF8String"));
                    string p = utf8 == IntPtr.Zero ? null : Marshal.PtrToStringUTF8(utf8);
                    return string.IsNullOrEmpty(p) ? null : p;
                }
                finally { objc_autoreleasePoolPop(pool); }
            }
        }
#endif

#if UNITY_STANDALONE_WIN && !UNITY_EDITOR
        static class Win32
        {
            [StructLayout(LayoutKind.Sequential)]
            struct OpenFileName
            {
                public int lStructSize;
                public IntPtr hwndOwner, hInstance, lpstrFilter, lpstrCustomFilter;
                public int nMaxCustFilter, nFilterIndex;
                public IntPtr lpstrFile;
                public int nMaxFile;
                public IntPtr lpstrFileTitle;
                public int nMaxFileTitle;
                public IntPtr lpstrInitialDir, lpstrTitle;
                public int Flags;
                public short nFileOffset, nFileExtension;
                public IntPtr lpstrDefExt, lCustData, lpfnHook, lpTemplateName, pvReserved;
                public int dwReserved, FlagsEx;
            }

            const int OFN_OVERWRITEPROMPT = 0x2, OFN_NOCHANGEDIR = 0x8, OFN_PATHMUSTEXIST = 0x800, OFN_FILEMUSTEXIST = 0x1000,
                OFN_EXPLORER = 0x80000;
            const int MaxPath = 4096;

            [DllImport("comdlg32.dll", EntryPoint = "GetSaveFileNameW", SetLastError = true)]
            static extern bool GetSaveFileName(ref OpenFileName ofn);

            [DllImport("comdlg32.dll", EntryPoint = "GetOpenFileNameW", SetLastError = true)]
            static extern bool GetOpenFileName(ref OpenFileName ofn);

            [DllImport("user32.dll")]
            static extern IntPtr GetActiveWindow();

            public static string Show(bool save, string defaultName, string extension, string titleText = null, string filterName = null,
                                      string[] extensions = null)
            {
                IntPtr filter = IntPtr.Zero, file = IntPtr.Zero, dir = IntPtr.Zero, title = IntPtr.Zero, defExt = IntPtr.Zero;
                try
                {
                    extensions ??= new[] { extension };
                    string patterns = string.Join(";", Array.ConvertAll(extensions, e => "*." + e));
                    filter = Marshal.StringToHGlobalUni($"{filterName ?? "Galaxy on Fire 2 saves"} ({patterns})\0{patterns}\0\0");
                    file = Marshal.AllocHGlobal(MaxPath * 2);
                    var name = new char[MaxPath];
                    if (!string.IsNullOrEmpty(defaultName)) defaultName.CopyTo(0, name, 0, Math.Min(defaultName.Length, MaxPath - 1));
                    Marshal.Copy(name, 0, file, MaxPath);
                    dir = Marshal.StringToHGlobalUni(StartFolder ?? "");
                    title = Marshal.StringToHGlobalUni(titleText ?? (save ? "Export saves" : "Import saves"));
                    defExt = Marshal.StringToHGlobalUni(extension);
                    var ofn = new OpenFileName
                    {
                        lStructSize = Marshal.SizeOf<OpenFileName>(),
                        hwndOwner = GetActiveWindow(),
                        lpstrFilter = filter,
                        nFilterIndex = 1,
                        lpstrFile = file,
                        nMaxFile = MaxPath,
                        lpstrInitialDir = dir,
                        lpstrTitle = title,
                        lpstrDefExt = defExt,
                        Flags = OFN_EXPLORER | OFN_NOCHANGEDIR | OFN_PATHMUSTEXIST | (save ? OFN_OVERWRITEPROMPT : OFN_FILEMUSTEXIST),
                    };
                    bool ok = save ? GetSaveFileName(ref ofn) : GetOpenFileName(ref ofn);
                    if (!ok) return null;   // cancelled (or CommDlgExtendedError; either way no file)
                    string path = Marshal.PtrToStringUni(file);
                    return string.IsNullOrEmpty(path) ? null : path;
                }
                catch (Exception e)
                {
                    Debug.LogWarning("FileDialog: " + e.Message);
                    return null;
                }
                finally
                {
                    foreach (var p in new[] { filter, file, dir, title, defExt })
                        if (p != IntPtr.Zero) Marshal.FreeHGlobal(p);
                }
            }
        }
#endif
    }
}
