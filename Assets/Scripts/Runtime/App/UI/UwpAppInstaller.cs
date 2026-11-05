// UwpAppInstaller.cs
// Remake-only: UWP's half of the in-game updater (GamePatcher, CLAUDE.md "Update patcher" in docs/notes/platforms.md). A
// packaged app can't change its own files; Windows' App Installer updates it from the release's .appinstaller file (the
// package on the GitHub release), downloading only the blocks that changed. Open hands the downloaded .appinstaller file to
// Windows (Launcher.LaunchFileAsync on the UI thread); if Windows won't open that file type from an app, the file's web
// address goes to the browser instead, which downloads it for the player to open. Elsewhere: not available.

using System;
using System.Threading.Tasks;

namespace GoF2Remake.UI
{
    public static class UwpAppInstaller
    {
        public static bool Available
        {
            get
            {
#if ENABLE_WINMD_SUPPORT
                return true;
#else
                return false;
#endif
            }
        }

        /// <summary>Opens the .appinstaller file at 'path' ('fallbackUrl': its web address, for the browser).</summary>
        public static Task<bool> Open(string path, string fallbackUrl = null)
        {
#if ENABLE_WINMD_SUPPORT
            var done = new TaskCompletionSource<bool>();
            UnityEngine.WSA.Application.InvokeOnUIThread(async () =>
            {
                try
                {
                    var file = await Windows.Storage.StorageFile.GetFileFromPathAsync(path);
                    bool opened = await Windows.System.Launcher.LaunchFileAsync(file);
                    if (!opened && !string.IsNullOrEmpty(fallbackUrl))
                        opened = await Windows.System.Launcher.LaunchUriAsync(new Uri(fallbackUrl));
                    done.TrySetResult(opened);
                }
                catch (Exception e)
                {
                    done.TrySetException(e);
                }
            }, false);
            return done.Task;
#else
            return Task.FromResult(false);
#endif
        }
    }
}
