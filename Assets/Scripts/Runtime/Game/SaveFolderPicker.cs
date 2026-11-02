// SaveFolderPicker.cs
// Remake-only: the system's folder picker for the save folder option (SaveFolder). Like ModImport.Pick it answers
// through a poll: at once for the modal dialogs, later where the picker runs elsewhere.
//   Editor    EditorUtility.OpenFolderPanel
//   Windows   shell32's SHBrowseForFolderW on a worker thread of its own (an STA for the dialog's COM)
//   Linux     zenity --directory, else kdialog --getexistingdirectory (a worker thread)
//   macOS     AppKit's NSOpenPanel in folder mode (FileDialog.OpenFolder, modal; osascript's choose folder lost the pick)
//   Android   SaveFolderActivity (the Storage Access Framework's ACTION_OPEN_DOCUMENT_TREE, a persistable grant)
//   iOS       the document picker in folder mode (Plugins/iOS/SaveFolderPicker.mm), kept as a security-scoped bookmark
//   UWP       FolderPicker, the folder kept in the FutureAccessList under a token of its own

using System;
using System.Runtime.InteropServices;
using UnityEngine;

namespace GoF2Remake.Data
{
    public static class SaveFolderPicker
    {
        /// <summary>What was picked: the target (a path or an Android tree) and its name for the player; or why not.</summary>
        public class Picked
        {
            public string target, name, error;
            public bool cancelled;
        }

        static string Title => Localization.Extra("saveFolderPickTitle", "Choose the save folder");

        static Picked FromPath(string path) => string.IsNullOrEmpty(path) ? new Picked { cancelled = true } : new Picked { target = path, name = path };

        /// <summary>Opens the picker; the poll is called until it answers (non-null).</summary>
        public static Func<Picked> Pick()
        {
            string title = Title;
#if UNITY_EDITOR
            var result = FromPath(UnityEditor.EditorUtility.OpenFolderPanel(title, Environment.GetFolderPath(Environment.SpecialFolder.MyDocuments), ""));
            return () => result;
#elif UNITY_STANDALONE_WIN
            IntPtr owner = Win32.GetActiveWindow();
            return OnThread(() => Win32.Browse(owner, title), true);
#elif UNITY_STANDALONE_LINUX
            string home = Environment.GetFolderPath(Environment.SpecialFolder.UserProfile);
            return OnThread(() =>
            {
                try { return RunPicker("zenity", $"--file-selection --directory --title=\"{title}\""); }
                catch (System.ComponentModel.Win32Exception)
                {
                    try { return RunPicker("kdialog", $"--title \"{title}\" --getexistingdirectory \"{home}\""); }
                    catch (System.ComponentModel.Win32Exception) { throw new Exception("no folder picker found (install zenity or kdialog)"); }
                }
            }, false);
#elif UNITY_STANDALONE_OSX
            Picked result;
            try { result = FromPath(FileDialog.OpenFolder(title)); }
            catch (Exception e) { result = new Picked { error = e.Message }; }
            return () => result;
#elif UNITY_ANDROID
            try
            {
                using var player = new AndroidJavaClass("com.unity3d.player.UnityPlayer");
                using var activity = player.GetStatic<AndroidJavaObject>("currentActivity");
                var picker = new AndroidJavaClass("com.joppietoppie.gof2remake.SaveFolderActivity");
                picker.CallStatic("start", activity);
                return () =>
                {
                    int state = picker.GetStatic<int>("state");
                    if (state == 0) return null;
                    var r = state == 1 ? new Picked { target = picker.GetStatic<string>("resultUri"), name = picker.GetStatic<string>("resultName") }
                          : state == 2 ? new Picked { cancelled = true }
                          : new Picked { error = picker.GetStatic<string>("error") };
                    picker.Dispose();
                    return r;
                };
            }
            catch (Exception e) { return () => new Picked { error = e.Message }; }
#elif UNITY_IOS
            try
            {
                GoF2SaveFolder_Pick();
                return () =>
                {
                    int state = GoF2SaveFolder_State();
                    if (state == 0) return null;
                    return state == 1 ? new Picked { target = SaveFolder.IosPrefix + GoF2SaveFolder_Result(), name = GoF2SaveFolder_Name() }
                         : state == 2 ? new Picked { cancelled = true }
                         : new Picked { error = GoF2SaveFolder_Error() };
                };
            }
            catch (Exception e) { return () => new Picked { error = e.Message }; }
#elif UNITY_WSA && ENABLE_WINMD_SUPPORT
            Picked result = null;
            UnityEngine.WSA.Application.InvokeOnUIThread(async () =>
            {
                Picked r;
                try
                {
                    var picker = new Windows.Storage.Pickers.FolderPicker { SuggestedStartLocation = Windows.Storage.Pickers.PickerLocationId.DocumentsLibrary };
                    picker.FileTypeFilter.Add("*");
                    var folder = await picker.PickSingleFolderAsync();
                    if (folder == null) r = new Picked { cancelled = true };
                    else
                    {
                        // A new token per pick: choosing another folder lets the old one's token go (SaveFolder.Choose).
                        string token = "saveFolder-" + Guid.NewGuid().ToString("N");
                        Windows.Storage.AccessCache.StorageApplicationPermissions.FutureAccessList.AddOrReplace(token, folder);
                        r = new Picked { target = SaveFolder.UwpPrefix + token, name = string.IsNullOrEmpty(folder.Path) ? folder.Name : folder.Path };
                    }
                }
                catch (Exception e) { r = new Picked { error = e.Message }; }
                System.Threading.Volatile.Write(ref result, r);
            }, false);
            return () => System.Threading.Volatile.Read(ref result);
#else
            return () => new Picked { error = "no folder picker on this platform" };
#endif
        }

#if UNITY_IOS && !UNITY_EDITOR
        [DllImport("__Internal")] static extern void GoF2SaveFolder_Pick();
        [DllImport("__Internal")] static extern int GoF2SaveFolder_State();
        [DllImport("__Internal")] static extern string GoF2SaveFolder_Result();
        [DllImport("__Internal")] static extern string GoF2SaveFolder_Name();
        [DllImport("__Internal")] static extern string GoF2SaveFolder_Error();
#endif

#if !UNITY_EDITOR && (UNITY_STANDALONE_WIN || UNITY_STANDALONE_LINUX)
        static Func<Picked> OnThread(Func<string> run, bool sta)
        {
            Picked result = null;
            var thread = new System.Threading.Thread(() =>
            {
                Picked r;
                try { r = FromPath(run()); }
                catch (Exception e) { r = new Picked { error = e.Message }; }
                System.Threading.Volatile.Write(ref result, r);
            }) { IsBackground = true, Name = "SaveFolder picker" };
            thread.Start();
            return () => System.Threading.Volatile.Read(ref result);
        }
#endif

#if !UNITY_EDITOR && UNITY_STANDALONE_LINUX
        static string RunPicker(string exe, string args)
        {
            var info = new System.Diagnostics.ProcessStartInfo(exe, args)
            {
                UseShellExecute = false,
                RedirectStandardOutput = true,
                RedirectStandardError = true,
                CreateNoWindow = true,
            };
            using var p = System.Diagnostics.Process.Start(info);
            string output = p.StandardOutput.ReadToEnd();
            p.StandardError.ReadToEnd();
            p.WaitForExit();
            return p.ExitCode == 0 ? output.Trim() : "";
        }
#endif

#if UNITY_STANDALONE_WIN && !UNITY_EDITOR
        static class Win32
        {
            [StructLayout(LayoutKind.Sequential, CharSet = CharSet.Unicode)]
            struct BrowseInfo
            {
                public IntPtr hwndOwner, pidlRoot, pszDisplayName;
                [MarshalAs(UnmanagedType.LPWStr)] public string lpszTitle;
                public uint ulFlags;
                public IntPtr lpfn, lParam;
                public int iImage;
            }

            const uint BIF_RETURNONLYFSDIRS = 0x1, BIF_EDITBOX = 0x10, BIF_NEWDIALOGSTYLE = 0x40;
            const int MaxPath = 32768;

            [DllImport("user32.dll")] public static extern IntPtr GetActiveWindow();
            [DllImport("shell32.dll", CharSet = CharSet.Unicode)] static extern IntPtr SHBrowseForFolderW(ref BrowseInfo bi);
            [DllImport("shell32.dll", CharSet = CharSet.Unicode)] static extern bool SHGetPathFromIDListEx(IntPtr pidl, IntPtr path, int size, int flags);
            [DllImport("ole32.dll")] static extern int CoInitializeEx(IntPtr reserved, uint coInit);
            [DllImport("ole32.dll")] static extern void CoUninitialize();
            [DllImport("ole32.dll")] static extern void CoTaskMemFree(IntPtr p);

            /// <summary>The folder dialog on this (worker) thread, made an STA first; "" when cancelled.</summary>
            public static string Browse(IntPtr owner, string title)
            {
                int init = CoInitializeEx(IntPtr.Zero, 0x2 /* COINIT_APARTMENTTHREADED */);
                IntPtr name = Marshal.AllocHGlobal(MaxPath * 2), path = Marshal.AllocHGlobal(MaxPath * 2);
                try
                {
                    var bi = new BrowseInfo
                    {
                        hwndOwner = owner,
                        pszDisplayName = name,
                        lpszTitle = title,
                        ulFlags = BIF_RETURNONLYFSDIRS | BIF_NEWDIALOGSTYLE | BIF_EDITBOX,
                    };
                    IntPtr pidl = SHBrowseForFolderW(ref bi);
                    if (pidl == IntPtr.Zero) return "";
                    try { return SHGetPathFromIDListEx(pidl, path, MaxPath, 0) ? Marshal.PtrToStringUni(path) : ""; }
                    finally { CoTaskMemFree(pidl); }
                }
                finally
                {
                    Marshal.FreeHGlobal(name);
                    Marshal.FreeHGlobal(path);
                    if (init >= 0) CoUninitialize();
                }
            }
        }
#endif
    }
}
