// MainMenuUpdate.cs
// Part of MainMenu: the "Update available" button (UpdateCheck) with the in-game updater (GamePatcher, CLAUDE.md "Update
// patcher" in docs/notes/platforms.md). Where the release carries this platform's patch, the button checks the game's
// files, says how much the update downloads (against the full zip), downloads it, then offers Restart now (the apply
// script swaps the files and starts the game again) or Later (applied when the game quits). Android: the same, then
// Install hands the rebuilt APK to Android's installer (after Android's one-time "install unknown apps" switch). UWP: the
// button opens Windows' App Installer on the release's .appinstaller. Each step runs in the menu's own dialog with Cancel;
// a failure offers the release page. Without a way to patch (a read-only folder, a release without a patch file) the
// button opens the release page as before.

using System.Collections;
using GoF2Remake.Data;
using UnityEngine;
using UnityEngine.UIElements;

namespace GoF2Remake.UI
{
    public partial class MainMenu
    {
        Coroutine updateRun;
        /// <summary>The update's progress dialog is the one showing (its text follows the progress).</summary>
        bool updateProgressShown;

        static string UpdateTitle => string.IsNullOrEmpty(UpdateCheck.LatestTag)
            ? X("updateAvailable", "Update available")
            : $"{X("updateAvailable", "Update available")}  ·  {UpdateCheck.LatestTag}";

        static string ReleasePageUrl => UpdateCheck.ReleaseUrl ?? UpdateCheck.ReleasesPage;

        static bool Apk => GamePatcher.KindHere == GamePatcher.Kind.Apk;

        void UpdatePressed()
        {
            if (GamePatcher.State == GamePatcher.Phase.Staged) { AskRestartForUpdate(); return; }
            if (updateRun != null) return;
            if (!GamePatcher.CanPatch) { Application.OpenURL(ReleasePageUrl); return; }
            updateRun = StartCoroutine(GamePatcher.KindHere == GamePatcher.Kind.AppInstaller ? RunAppInstallerUpdate() : RunUpdate());
        }

        /// <summary>"12.3 MB" / "1.4 GB".</summary>
        public static string SizeText(long bytes)
        {
            double mb = bytes / 1048576.0;
            if (mb >= 1000) return (mb / 1024.0).ToString("0.0", System.Globalization.CultureInfo.InvariantCulture) + " GB";
            return mb.ToString(mb < 10 ? "0.0" : "0", System.Globalization.CultureInfo.InvariantCulture) + " MB";
        }

        static string Percent(float f) => Mathf.FloorToInt(Mathf.Clamp01(f) * 100f).ToString();

        /// <summary>"0:42" / "12:05" / "1:02:09" (a clock reads the same in every language).</summary>
        public static string ClockText(float seconds)
        {
            int s = Mathf.Max(0, Mathf.CeilToInt(seconds));
            return s >= 3600 ? $"{s / 3600}:{s / 60 % 60:00}:{s % 60:00}" : $"{s / 60}:{s % 60:00}";
        }

        static string UpdateProgressText()
        {
            switch (GamePatcher.State)
            {
                case GamePatcher.Phase.Downloading:
                    long total = GamePatcher.Install?.DownloadBytes ?? 0;
                    string text = string.Format(X("patchDownloading", "Downloading… {0} of {1}"),
                        SizeText(GamePatcher.Transferred), SizeText(total));
                    if (GamePatcher.BytesPerSecond > 0f && GamePatcher.SecondsLeft >= 0f)
                        text += "\n" + string.Format(X("patchSpeed", "{0}/s · {1} left"),
                            SizeText((long)GamePatcher.BytesPerSecond), ClockText(GamePatcher.SecondsLeft));
                    return text;
                case GamePatcher.Phase.Assembling:
                    return string.Format(X("patchAssembling", "Preparing the new files… {0} %"), Percent(GamePatcher.Progress));
                case GamePatcher.Phase.Fetching when GamePatcher.KindHere == GamePatcher.Kind.AppInstaller:
                    return X("patchOpening", "Opening Windows' App Installer…");
                default:
                    return string.Format(X("patchChecking", "Checking which files changed… {0} %"), Percent(GamePatcher.Progress));
            }
        }

        /// <summary>The dialog while a step runs: its text follows the progress, its one button (and Back) cancels.</summary>
        void ShowUpdateProgress()
        {
            ShowNotice(UpdateTitle, UpdateProgressText());
            var yes = root.Q<Button>("dialogYes");
            InputGlyph.SetText(yes, Localization.Get(425).UiCaps());   // Cancel
            dialogYes = dialogNo = () => { updateProgressShown = false; GamePatcher.Cancel(); };
            updateProgressShown = true;
        }

        void CloseUpdateProgress()
        {
            if (!updateProgressShown) return;
            updateProgressShown = false;
            CloseDialog();
        }

        IEnumerator FollowUpdate(IEnumerator step)
        {
            var run = StartCoroutine(step);
            yield return null;
            var bar = root.Q("dialogProgress");
            var fill = root.Q("dialogProgressFill");
            while (GamePatcher.Working)
            {
                if (updateProgressShown)
                {
                    root.Q<Label>("dialogText").text = UpdateProgressText();
                    // The bar while there is something to measure: checking the files, downloading, preparing them.
                    var phase = GamePatcher.State;
                    bool measured = phase == GamePatcher.Phase.Scanning || phase == GamePatcher.Phase.Downloading || phase == GamePatcher.Phase.Assembling;
                    bar?.EnableInClassList("dialog-progress--shown", measured);
                    if (fill != null) fill.style.width = Length.Percent(Mathf.Clamp01(GamePatcher.Progress) * 100f);
                }
                yield return null;
            }
            bar?.RemoveFromClassList("dialog-progress--shown");
            yield return run;
        }

        IEnumerator RunUpdate()
        {
            try
            {
                ShowUpdateProgress();
                yield return FollowUpdate(GamePatcher.Prepare());
                if (!updateProgressShown) yield break;   // cancelled
                CloseUpdateProgress();
                if (GamePatcher.State == GamePatcher.Phase.Failed) { ShowUpdateFailed(); yield break; }
                var install = GamePatcher.Install;
                if (GamePatcher.State != GamePatcher.Phase.Ready || install == null) yield break;
                if (install.UpToDate)
                {
                    ShowNotice(UpdateTitle, string.Format(X("patchUpToDate", "The game's files already match {0}."), install.Manifest.version));
                    yield break;
                }
                long full = GamePatcher.FullSize;
                string text = full > install.DownloadBytes
                    ? string.Format(Apk
                            ? X("patchReadyAndroid", "This update downloads {0} instead of the full {1}, then Android installs it.")
                            : X("patchReady", "This update downloads {0} instead of the full {1}, then restarts the game."),
                        SizeText(install.DownloadBytes), SizeText(full))
                    : string.Format(Apk
                            ? X("patchReadyAndroidShort", "This update downloads {0}, then Android installs it.")
                            : X("patchReadyShort", "This update downloads {0}, then restarts the game."),
                        SizeText(install.DownloadBytes));
                bool go = false;
                ShowChoice(UpdateTitle, text, X("patchUpdate", "Update"), () => go = true,
                    X("patchReleasePage", "Release page"), () => Application.OpenURL(ReleasePageUrl));
                while (dialog.ClassListContains("dialog-backdrop--shown")) yield return null;
                if (!go) yield break;

                ShowUpdateProgress();
                yield return FollowUpdate(GamePatcher.Download());
                if (!updateProgressShown) yield break;
                CloseUpdateProgress();
                if (GamePatcher.State == GamePatcher.Phase.Failed) { ShowUpdateFailed(); yield break; }
                if (GamePatcher.State == GamePatcher.Phase.Staged) AskRestartForUpdate();
            }
            finally
            {
                updateRun = null;
            }
        }

        void ShowUpdateFailed()
        {
            string reason = GamePatcher.NoSpace && long.TryParse(GamePatcher.Error, out long needed)
                ? string.Format(X("patchNoSpace", "Not enough free disk space: the update needs {0}."), SizeText(needed))
                : string.Format(X("patchFailed", "The update didn't work ({0}). The full game is on the release page."), GamePatcher.Error);
            ShowDialog(UpdateTitle, reason, () => Application.OpenURL(ReleasePageUrl));
            InputGlyph.SetText(root.Q<Button>("dialogYes"), X("patchReleasePage", "Release page").UiCaps());
            InputGlyph.SetText(root.Q<Button>("dialogNo"), Localization.Get(425).UiCaps());   // Cancel
            dialogRelabelled = true;
        }

        /// <summary>UWP: Windows' App Installer updates the package (it downloads only the changed blocks itself).</summary>
        IEnumerator RunAppInstallerUpdate()
        {
            try
            {
                bool go = false;
                ShowChoice(UpdateTitle, X("patchUwp", "Windows downloads only the changed parts of this update and installs it. The game closes while it does."),
                    X("patchUpdate", "Update"), () => go = true,
                    X("patchReleasePage", "Release page"), () => Application.OpenURL(ReleasePageUrl));
                while (dialog.ClassListContains("dialog-backdrop--shown")) yield return null;
                if (!go) yield break;
                ShowUpdateProgress();
                yield return FollowUpdate(GamePatcher.OpenAppInstaller());
                if (!updateProgressShown) yield break;
                CloseUpdateProgress();
                if (GamePatcher.State == GamePatcher.Phase.Failed) ShowUpdateFailed();
            }
            finally
            {
                updateRun = null;
            }
        }

        /// <summary>Android: the rebuilt APK to the installer, once Android lets this app install apps.</summary>
        void AskInstallForUpdate()
        {
            ShowDialog(UpdateTitle, X("patchStagedAndroid", "The update is ready. Install it now? Android asks you to confirm."),
                () =>
                {
                    if (!AndroidUpdateInstaller.CanInstall)
                    {
                        ShowDialog(UpdateTitle, X("patchAllowInstalls",
                                "Android asks once to let this game install its updates. Allow it, come back and press Update available again."),
                            AndroidUpdateInstaller.OpenInstallSettings);
                        InputGlyph.SetText(root.Q<Button>("dialogYes"), X("patchOpenSettings", "Open settings").UiCaps());
                        InputGlyph.SetText(root.Q<Button>("dialogNo"), Localization.Get(425).UiCaps());   // Cancel
                        dialogRelabelled = true;
                        Select(root.Q<Button>("dialogYes"));
                        return;
                    }
                    if (!GamePatcher.StartApply(true)) ShowUpdateFailed();
                });
            InputGlyph.SetText(root.Q<Button>("dialogYes"), X("patchInstall", "Install").UiCaps());
            InputGlyph.SetText(root.Q<Button>("dialogNo"), X("patchLater", "Later").UiCaps());
            dialogRelabelled = true;
            Select(root.Q<Button>("dialogYes"));
        }

        void AskRestartForUpdate()
        {
            if (Apk) { AskInstallForUpdate(); return; }
            ShowDialog(UpdateTitle, X("patchStaged", "The update is ready. Restart the game now to finish it? Otherwise it is installed when you quit."),
                () =>
                {
                    if (GamePatcher.StartApply(true)) Quit();
                    else ShowUpdateFailed();
                });
            dialogNo = GamePatcher.ApplyOnQuit;
            InputGlyph.SetText(root.Q<Button>("dialogYes"), X("patchRestart", "Restart now").UiCaps());
            InputGlyph.SetText(root.Q<Button>("dialogNo"), X("patchLater", "Later").UiCaps());
            dialogRelabelled = true;
            Select(root.Q<Button>("dialogYes"));
        }
    }
}
