// IssueReportUpload.cs
// Remake-only: an issue report (IssueReport) sent to the project's Unity dashboard through Unity's User Reporting package
// (com.unity.services.user-reporting 2.0, Cloud Diagnostics > User Reporting; the project is linked to Unity Cloud for
// Relay / Lobby already). Unity Services start on the first report when the player hasn't been online yet
// (NetGame.StartServices, no sign-in). Per report the package's client is set up afresh (its attachments and screenshots
// would carry over to the next), with its per-frame metrics off: the screenshot goes in as the report's picture (its own
// thumbnail in the dashboard, at most 1920 x 1080) and every file as an attachment, the title as the summary, the
// description as its "Description" field, the version and platform as dimensions (the dashboard's filters).
// The code is behind GOF2_USER_REPORTING, a define of GoF2.App.asmdef that is set while the package is installed:
// without the package the game still compiles, and a report is only saved (Supported is false).

using System;
using System.Collections.Generic;
using System.Threading.Tasks;
using UnityEngine;
#if GOF2_USER_REPORTING
using Unity.Services.UserReporting;
using Unity.Services.UserReporting.Client;
#endif

namespace GoF2Remake.UI
{
    [Unity.Scripting.LifecycleManagement.NoAutoStaticsCleanup]
    public static class IssueReportUpload
    {
        /// <summary>One attachment's and a whole report's size limits (the report is one JSON post, its files as base64):
        /// a file over the first is left out, as are the files that would pass the second.</summary>
        public const int MaxAttachment = 3 * 1024 * 1024, MaxTotal = 8 * 1024 * 1024;
        /// <summary>A report that hasn't arrived after this long counts as failed (the zip is on disk either way).</summary>
        const float TimeoutSeconds = 90f;
        /// <summary>The screenshot's size in the report.</summary>
        const int ShotWidth = 1920, ShotHeight = 1080;

        /// <summary>This build can send reports (the User Reporting package is in it).</summary>
        public static bool Supported =>
#if GOF2_USER_REPORTING
            true;
#else
            false;
#endif

        /// <summary>Sending can be tried now: the build can, the project has its Unity Cloud id and a network is there.</summary>
        public static bool Available => Supported && !string.IsNullOrEmpty(Application.cloudProjectId)
                                        && Application.internetReachability != NetworkReachability.NotReachable;

        /// <summary>A report is on its way.</summary>
        public static bool Sending { get; private set; }

        [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.SubsystemRegistration)]
        static void ResetStatics() => Sending = false;

        /// <summary>The files that go along within the size limits, in their order.</summary>
        public static List<IssueReport.Entry> Fitting(List<IssueReport.Entry> files)
        {
            var fit = new List<IssueReport.Entry>();
            long total = 0;
            if (files == null) return fit;
            foreach (var f in files)
            {
                if (f?.data == null || f.data.Length > MaxAttachment || total + f.data.Length > MaxTotal) continue;
                total += f.data.Length;
                fit.Add(f);
            }
            return fit;
        }

        /// <summary>Sends the report; 'progress' 0..1 while it uploads, 'done' once with whether it arrived (both on the
        /// main thread). 'screenshot' (may be null) stays the caller's.</summary>
        public static async void Send(string title, string description, List<IssueReport.Entry> files, Texture2D screenshot,
                                      Action<float> progress, Action<bool> done)
        {
            if (Sending || !Available) { done?.Invoke(false); return; }
            Sending = true;
            bool ok = false;
            try { ok = await SendAsync(title, description, files, screenshot, progress); }
            catch (Exception e) { Debug.LogWarning("IssueReportUpload: sending failed (" + e.Message + ")"); }
            Sending = false;
            Debug.Log(ok ? "IssueReportUpload: report sent" : "IssueReportUpload: report not sent");
            done?.Invoke(ok);
        }

#if GOF2_USER_REPORTING
        static async Task<bool> SendAsync(string title, string description, List<IssueReport.Entry> files, Texture2D screenshot, Action<float> progress)
        {
            await Multiplayer.NetGame.StartServices();
            var reporting = UserReportingService.Instance;
            // A new client per report: the last one's attachments and screenshots would go along again. No per-frame metrics.
            reporting.Configure(new UserReportingClientConfiguration(metricsGatheringMode: MetricsGatheringMode.Disabled));
            reporting.ClearOngoingReport();

            foreach (var f in Fitting(files))
                if (f.name != "screenshot.jpg" || screenshot == null)   // the picture goes in as the report's screenshot
                    reporting.AddAttachmentToReport(f.name, f.name, f.data, f.mediaType ?? "");

            if (screenshot != null) await AddScreenshot(reporting, screenshot);

            reporting.CreateNewUserReport();
            if (!reporting.HasOngoingReport) return false;
            reporting.SetReportSummary(string.IsNullOrEmpty(title) ? "Issue report" : title);
            reporting.SetReportDescription(description ?? "");
            reporting.AddDimensionValue("Version", BuildVersion.Text);
            reporting.AddDimensionValue("Platform", Application.platform.ToString());
            reporting.AddDimensionValue("Language", GoF2Remake.Data.Localization.Language);

            bool finished = false, arrived = false;
            reporting.SendUserReport(p => progress?.Invoke(Mathf.Clamp01(p)), success => { arrived = success; finished = true; });
            float deadline = Time.realtimeSinceStartup + TimeoutSeconds;
            while (!finished && Time.realtimeSinceStartup < deadline) await Task.Yield();
            reporting.ClearOngoingReport();
            return finished && arrived;
        }

        /// <summary>The package takes its screenshots from the screen, a camera or a render texture over a few frames (read,
        /// resize, encode): the picture taken before the form opened goes through a render texture, and the report waits
        /// (at most two seconds) until the package has it.</summary>
        static async Task AddScreenshot(IUserReporting reporting, Texture2D screenshot)
        {
            var rt = RenderTexture.GetTemporary(screenshot.width, screenshot.height, 0);
            try
            {
                Graphics.Blit(screenshot, rt);
                reporting.TakeScreenshot(ShotWidth, ShotHeight, rt);
                float deadline = Time.realtimeSinceStartup + 2f;
                int frame = 0;
                while (Time.realtimeSinceStartup < deadline)
                {
                    await Task.Yield();
                    if (++frame % 4 != 0) continue;   // its stages take a frame each
                    var latest = reporting.GetLatestScreenshot();   // a new texture each call
                    if (latest == null) continue;
                    UnityEngine.Object.Destroy(latest);
                    break;
                }
            }
            finally { RenderTexture.ReleaseTemporary(rt); }
        }
#else
        static Task<bool> SendAsync(string title, string description, List<IssueReport.Entry> files, Texture2D screenshot, Action<float> progress) =>
            Task.FromResult(false);
#endif
    }
}
