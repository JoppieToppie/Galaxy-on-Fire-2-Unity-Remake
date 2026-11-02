// ModImport.cs
// Remake-only: the mod browser's Import mod and Delete (players found the Android Mods folder, Android/data/<package>/
// files/Mods, hard to reach). Import: the system's file picker, the picked file copied into temporaryCachePath/ModImport:
//   Android   ModImportActivity (Assets/Plugins/Android): the document picker, which does the copy itself
//   Windows   FileDialog's comdlg32 open dialog (modal, like the saves' import)
//   Linux     zenity, else kdialog (a worker thread runs it; neither installed: an error naming the Mods folder)
//   macOS     AppKit's NSOpenPanel (FileDialog, modal like Windows; osascript's "choose file" on a worker thread lost the pick)
//   UWP       Windows.Storage.Pickers.FileOpenPicker on the UI thread
//   iOS       the document picker (Plugins/iOS/FileImportPicker.mm), which copies the file itself; the Files app also
//             shows the game's Documents folder (Mods inside it, IosPlayerSetup's UIFileSharingEnabled)
//   Editor    EditorUtility's open panel The file is checked as a mod archive (a .zip with a readable mod.json, ModSource's
// layout), then moved into ModManager.MainFolder (a phone's user folder) as "<id>.zip"; an
// installed mod with the same id is replaced in its own folder and keeps its on / off and load order (the browser asks
// first). New mods start off, like one copied in by hand. Delete: the mod turned off (and the mods that need it), its
// archive released, then its file or folder deleted; the next scan drops its caches (ModManager.Scan's prunes).

using System;
using System.IO;
using UnityEngine;

namespace GoF2Remake.Modding
{
    public static class ModImport
    {
        /// <summary>A file picker exists here (not on the platforms without one).</summary>
        public static bool Available
        {
            get
            {
#if UNITY_EDITOR || UNITY_ANDROID || UNITY_IOS || UNITY_STANDALONE || (UNITY_WSA && ENABLE_WINMD_SUPPORT)
                return true;
#else
                return false;
#endif
            }
        }

        const string Title = "Import mod";
        /// <summary>A mod archive's file types (ModSource.Open).</summary>
        public static readonly string[] Extensions = { "zip" };

        /// <summary>A file the player picked copied into the import folder (the original stays where it is).</summary>
        static Picked CopyIn(string source, string folder)
        {
            try
            {
                Directory.CreateDirectory(folder);
                string copy = Path.Combine(folder, "picked.tmp");
                File.Copy(source, copy, true);
                return new Picked { path = copy, name = Path.GetFileName(source) };
            }
            catch (Exception e) { return new Picked { error = e.Message }; }
        }

#if UNITY_STANDALONE_LINUX
        /// <summary>Runs a picker program and returns what it printed (the path), "" when cancelled; throws
        /// Win32Exception when the program isn't installed.</summary>
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

        /// <summary>The picker program on a worker thread; the poll answers once it has.</summary>
        static Func<Picked> PickWithProgram(Func<string> run, string folder)
        {
            Picked result = null;
            var thread = new System.Threading.Thread(() =>
            {
                Picked r;
                try
                {
                    string path = run();
                    r = string.IsNullOrEmpty(path) ? new Picked { cancelled = true } : CopyIn(path, folder);
                }
                catch (Exception e) { r = new Picked { error = e.Message }; }
                System.Threading.Volatile.Write(ref result, r);
            }) { IsBackground = true, Name = "ModImport picker" };
            thread.Start();
            return () => System.Threading.Volatile.Read(ref result);
        }
#endif

#if UNITY_IOS && !UNITY_EDITOR
        [System.Runtime.InteropServices.DllImport("__Internal")] static extern void GoF2FilePick_Start(string folder, string extensions);
        [System.Runtime.InteropServices.DllImport("__Internal")] static extern int GoF2FilePick_State();
        [System.Runtime.InteropServices.DllImport("__Internal")] static extern string GoF2FilePick_Result();
        [System.Runtime.InteropServices.DllImport("__Internal")] static extern string GoF2FilePick_Name();
        [System.Runtime.InteropServices.DllImport("__Internal")] static extern string GoF2FilePick_Error();
#endif

        static string ImportFolder => Path.Combine(Application.temporaryCachePath, "ModImport");

        /// <summary>What was picked: a copy of the file in the import folder and its own name; or why not.</summary>
        public class Picked
        {
            public string path, name, error;
            public bool cancelled;
        }

        /// <summary>Opens the picker; the returned poll is called until it answers (non-null): at once for the modal
        /// dialogs (the Editor, Windows, macOS), when the activity / program / picker is done elsewhere.</summary>
        public static Func<Picked> Pick() => Pick(Title, "Galaxy on Fire 2 mods", Extensions);

        /// <summary>The picker for any file kind (the Mod Builder's models): 'title', the filter's name and its extensions
        /// (without the dot). The Android picker shows every file (its activity takes any type).</summary>
        public static Func<Picked> Pick(string title, string filterName, string[] extensions)
        {
            string folder = ImportFolder;   // temporaryCachePath: main thread only
            string Title = title;
            string firstExtension = extensions.Length > 0 ? extensions[0] : "*";
#if UNITY_EDITOR
            string p = UnityEditor.EditorUtility.OpenFilePanelWithFilters(Title,
                Environment.GetFolderPath(Environment.SpecialFolder.MyDocuments), new[] { filterName, string.Join(",", extensions), "All files", "*" });
            var result = string.IsNullOrEmpty(p) ? new Picked { cancelled = true } : CopyIn(p, folder);
            return () => result;
#elif UNITY_STANDALONE_WIN
            string p = Data.FileDialog.Open(Title, filterName, extensions);
            var result = string.IsNullOrEmpty(p) ? new Picked { cancelled = true } : CopyIn(p, folder);
            return () => result;
#elif UNITY_STANDALONE_LINUX
            string home = Environment.GetFolderPath(Environment.SpecialFolder.UserProfile);
            return PickWithProgram(() =>
            {
                try
                {
                    return RunPicker("zenity", $"--file-selection --title=\"{Title}\" --file-filter=\"{filterName} | *.{firstExtension}\" --file-filter=\"All files | *\"");
                }
                catch (System.ComponentModel.Win32Exception)   // no zenity: KDE's kdialog
                {
                    try { return RunPicker("kdialog", $"--title \"{Title}\" --getopenfilename \"{home}\" \"*.{firstExtension}|{filterName}\""); }
                    catch (System.ComponentModel.Win32Exception)
                    {
                        throw new Exception("no file picker found (install zenity or kdialog), or copy the mod into " + ModManager.MainFolder);
                    }
                }
            }, folder);
#elif UNITY_STANDALONE_OSX
            Picked result;
            try
            {
                string p = Data.FileDialog.Open(Title, filterName, extensions);
                result = string.IsNullOrEmpty(p) ? new Picked { cancelled = true } : CopyIn(p, folder);
            }
            catch (Exception e) { result = new Picked { error = e.Message }; }
            return () => result;
#elif UNITY_WSA && ENABLE_WINMD_SUPPORT
            Picked result = null;
            try { Directory.CreateDirectory(folder); } catch { }
            UnityEngine.WSA.Application.InvokeOnUIThread(async () =>
            {
                Picked r;
                try
                {
                    var picker = new Windows.Storage.Pickers.FileOpenPicker
                    {
                        SuggestedStartLocation = Windows.Storage.Pickers.PickerLocationId.Downloads,
                        ViewMode = Windows.Storage.Pickers.PickerViewMode.List,
                    };
                    foreach (var ext in extensions) picker.FileTypeFilter.Add("." + ext);
                    var file = await picker.PickSingleFileAsync();
                    if (file == null) r = new Picked { cancelled = true };
                    else
                    {
                        var target = await Windows.Storage.StorageFolder.GetFolderFromPathAsync(folder);
                        var copy = await file.CopyAsync(target, "picked.tmp", Windows.Storage.NameCollisionOption.ReplaceExisting);
                        r = new Picked { path = copy.Path, name = file.Name };
                    }
                }
                catch (Exception e) { r = new Picked { error = e.Message }; }
                System.Threading.Volatile.Write(ref result, r);
            }, false);
            return () => System.Threading.Volatile.Read(ref result);
#elif UNITY_ANDROID
            try
            {
                using var player = new AndroidJavaClass("com.unity3d.player.UnityPlayer");
                using var activity = player.GetStatic<AndroidJavaObject>("currentActivity");
                var picker = new AndroidJavaClass("com.joppietoppie.gof2remake.ModImportActivity");
                picker.CallStatic("start", activity, folder);
                return () =>
                {
                    int state = picker.GetStatic<int>("state");
                    if (state == 0) return null;
                    var r = state == 1 ? new Picked { path = picker.GetStatic<string>("resultPath"), name = picker.GetStatic<string>("resultName") }
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
                GoF2FilePick_Start(folder, string.Join(",", extensions));
                return () =>
                {
                    int state = GoF2FilePick_State();
                    if (state == 0) return null;
                    return state == 1 ? new Picked { path = GoF2FilePick_Result(), name = GoF2FilePick_Name() }
                         : state == 2 ? new Picked { cancelled = true }
                         : new Picked { error = GoF2FilePick_Error() };
                };
            }
            catch (Exception e) { return () => new Picked { error = e.Message }; }
#else
            return () => new Picked { error = "no file picker on this platform" };
#endif
        }

        /// <summary>The picked file read as a mod: its manifest (null with an error when it isn't one).</summary>
        public static ModManifest Inspect(string path, out string error)
        {
            error = null;
            try
            {
                using var src = new ZipSource(path);
                if (!src.Exists(ModManifest.FileName)) { error = "no mod.json"; return null; }
                return ModManifest.Read(src);
            }
            catch (ModJsonException e) { error = e.Message; }
            catch (Exception e) { error = e is InvalidDataException ? "not a zip archive" : e.Message; }
            return null;
        }

        /// <summary>Moves the picked file into the user Mods folder as the mod 'manifest' names, replacing the installed mod
        /// with that id. Returns the installed path, or null with the error.</summary>
        public static string Install(string picked, string originalName, ModManifest manifest, out string error)
        {
            error = null;
            try
            {
                // A replaced mod keeps its place: its folder, on / off and load order (manifest.id is checked by
                // ModManifest.Read: a-z 0-9 _ -, so it is a safe file name).
                var old = ModManager.Find(manifest.id);
                string folder = old != null ? ModManager.FolderOf(old) : ModManager.MainFolder;
                if (old != null && !Delete(old, out error, keepEnabled: true)) return null;
                Directory.CreateDirectory(folder);
                string target = Path.Combine(folder, manifest.id + ".zip");
                for (int i = 2; File.Exists(target) || Directory.Exists(target); i++)   // another file by that name (not a mod)
                    target = Path.Combine(folder, $"{manifest.id}_{i}.zip");
                File.Move(picked, target);
                Debug.Log($"Mods: imported \"{manifest.id}\" {manifest.version} from {originalName} to {target}");
                ModManager.Scan();
                return target;
            }
            catch (Exception e)
            {
                error = e.Message;
                Debug.LogWarning($"Mods: import of {originalName} failed: {e.Message}");
                return null;
            }
            finally { Discard(picked); }
        }

        /// <summary>Deletes the picked copy (cancelled, refused, or after a failed move).</summary>
        public static void Discard(string picked)
        {
            try { if (!string.IsNullOrEmpty(picked) && File.Exists(picked)) File.Delete(picked); } catch { }
        }

        /// <summary>Turns the mod off (and what needs it; not 'keepEnabled': a replacement), closes its archive and deletes
        /// its file or folder; the mods are scanned again either way.</summary>
        public static bool Delete(ModInfo mod, out string error, bool keepEnabled = false)
        {
            error = null;
            if (mod?.Source == null) { error = "not installed"; return false; }
            string location = mod.Source.Location;
            try
            {
                if (!keepEnabled && ModManager.IsEnabled(mod.Id)) ModManager.SetEnabled(mod.Id, false);
                mod.Release();   // a zip mod keeps its archive open
                if (Directory.Exists(location)) Directory.Delete(location, true);
                else if (File.Exists(location)) File.Delete(location);
                Debug.Log($"Mods: deleted \"{mod.Id}\" ({location})");
                return true;
            }
            catch (Exception e)
            {
                error = e.Message;
                Debug.LogWarning($"Mods: can't delete {location}: {e.Message}");
                return false;
            }
            finally { ModManager.Scan(); }
        }
    }
}
