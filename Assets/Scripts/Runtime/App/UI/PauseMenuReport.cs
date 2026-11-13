// PauseMenuReport.cs
// Remake-only: the pause menu's "Report an issue" (a part of PauseMenu). Choosing it hides the menu for one frame and takes
// the screen's picture as it was under it (ScreenshotKey.Grab), then shows the form: what the report carries, the
// picture, a text box for what went wrong, "Include current game" and Send report. Sending collects the report
// (IssueReport.Collect), keeps it as a zip in the IssueReports folder and uploads it to the project's Unity dashboard
// (IssueReportUpload); without a connection, or when the upload fails, the result page names the zip and opens a filled-in
// GitHub issue to attach it to (desktop).
// The text box is an entry of the menu like the others: Enter / A / a click starts typing (a controller without a system
// keyboard gets PadKeyboard), Esc ends it. While it is typed into the menu's and the game's keys rest
// (GameControls.TextEntryOpen, NetChat.SetTyping), as with an option's value box (OptionControl.BeginEdit).

using System;
using System.IO;
using System.Text;
using GoF2Remake.Data;
using GoF2Remake.Flight;
using UnityEngine;
using UnityEngine.UIElements;

namespace GoF2Remake.UI
{
    public partial class PauseMenu
    {
        /// <summary>The screen under the menu when the form was opened; destroyed when the report pages are left.</summary>
        Texture2D reportShot;
        /// <summary>What the player typed: kept when the form is left without sending, emptied once a report has gone.</summary>
        string reportText = "";
        bool reportIncludeSave = true;
        /// <summary>The menu is hidden for the frame the picture is taken in: no keys meanwhile (Tick).</summary>
        bool reportCapturing;
        bool reportTyping, reportTookTyping;
        /// <summary>The last report: uploaded, tried to, and where its zip is (null when it couldn't be written).</summary>
        bool reportSent, reportTried;
        string reportPath;
        /// <summary>A line over the form's buttons (the description is still empty).</summary>
        string reportNotice;
        TextField reportField;
        Label reportProgress;

        static string Issue(string key, string english) => Localization.Extra(key, english);

        void BeginReport()
        {
            reportNotice = null;
            reportCapturing = true;
            backdrop.RemoveFromClassList("pause-backdrop--shown");   // the picture shows the game, not this menu
            ScreenshotKey.Grab(shot =>
            {
                reportCapturing = false;
                if (!IsOpen) { if (shot != null) UnityEngine.Object.Destroy(shot); return; }
                if (reportShot != null) UnityEngine.Object.Destroy(reportShot);
                reportShot = shot;
                backdrop.AddToClassList("pause-backdrop--shown");
                Show(Page.Report);
                openedFrame = Time.frameCount;   // the key that chose the entry
            });
        }

        void BuildReport()
        {
            // The picture that goes along, small, with what the report carries beside it.
            var top = new VisualElement { pickingMode = PickingMode.Ignore };
            top.AddToClassList("pause-report-top");
            if (reportShot != null)
            {
                var shot = new Image { image = reportShot, scaleMode = ScaleMode.ScaleToFit, pickingMode = PickingMode.Ignore };
                shot.AddToClassList("pause-report-shot");
                top.Add(shot);
            }
            var intro = new Label(Issue("issueIntro", "Describe what went wrong and what you were doing when it happened. The report goes to the developers "
                                                  + "with this screenshot, the game's log, your system's details, your settings and your mods."))
            { pickingMode = PickingMode.Ignore };
            intro.AddToClassList("pause-text");
            intro.AddToClassList("pause-report-intro");
            top.Add(intro);
            body.Add(top);
            var field = new TextField { multiline = true, maxLength = IssueReport.MaxDescription, verticalScrollerVisibility = ScrollerVisibility.Auto };
            field.AddToClassList("gof-field");
            field.AddToClassList("pause-report-field");
            field.textEdition.placeholder = Issue("issuePlaceholder", "What happened?");
            field.SetValueWithoutNotify(reportText);
            field.RegisterValueChangedCallback(e => reportText = e.newValue ?? "");
            TextFieldKeys.Guard(field);
            field.RegisterCallback<FocusInEvent>(_ => StartReportTyping());
            field.RegisterCallback<FocusOutEvent>(_ => StopReportTyping());
            body.Add(field);
            items.Add(field);
            actions.Add(EditReportText);
            reportField = field;
            if (!string.IsNullOrEmpty(reportNotice)) Text(reportNotice, "pause-heading--red");
            string include = string.Format(Issue("issueIncludeSave", "Include current game: {0}"), Localization.Get(reportIncludeSave ? 134 : 135));
            Item(include.UiCaps(), () =>
            {
                reportIncludeSave = !reportIncludeSave;
                int keep = index;
                Show(Page.Report);
                index = keep;
                Highlight();
            });
            Item((IssueReportUpload.Available ? Issue("issueSend", "Send report") : Issue("issueSave", "Save report")).UiCaps(), SendReport);
        }

        /// <summary>Enter / A / a click on the text box: typing starts (a frame later, so the Enter that chose it isn't
        /// typed into it); a controller on a platform without a system keyboard gets the on-screen one.</summary>
        void EditReportText()
        {
            var field = reportField;
            if (field == null) return;
            if (InputMode.Current == InputKind.Gamepad && !TouchScreenKeyboard.isSupported) { PadKeyboard.Open(root, field); return; }
            field.schedule.Execute(() => { if (reportField == field && field.panel != null) field.Focus(); }).StartingIn(0);
        }

        void StartReportTyping()
        {
            if (reportTyping) return;
            reportTyping = true;
            reportTookTyping = !Multiplayer.NetChat.Typing;
            if (reportTookTyping) Multiplayer.NetChat.SetTyping(true);
            GameControls.TextEntryOpen = true;
        }

        void StopReportTyping()
        {
            if (!reportTyping) return;
            reportTyping = false;
            if (reportTookTyping) Multiplayer.NetChat.SetTyping(false);
            reportTookTyping = false;
            GameControls.TextEntryOpen = false;
        }

        /// <summary>The report pages are left (another page, the menu closed): typing ends and the picture goes, unless an
        /// upload still reads it (the next report replaces it).</summary>
        void LeaveReport()
        {
            StopReportTyping();
            reportField = null;
            reportProgress = null;
            if (reportShot != null && !IssueReportUpload.Sending)
            {
                UnityEngine.Object.Destroy(reportShot);
                reportShot = null;
            }
        }

        /// <summary>Where the ship is, for report.txt.</summary>
        string ReportContext()
        {
            var sb = new StringBuilder();
            try
            {
                if (level == null) return null;
                var station = level.Database != null ? level.Database.Stations.Find(s => s.index == Session.StationIndex) : null;
                if (station != null) sb.Append("Orbit: ").Append(station.name).Append(" (").Append(station.systemName).Append(")\n");
                if (level.Player != null) sb.Append("Ship position: ").Append(level.Player.transform.position.ToString("0")).Append('\n');
                sb.Append("Docked in orbit: ").Append(level.DockedInOrbit ? "yes" : "no").Append(", cutscene: ").Append(level.Cutscene ? "yes" : "no").Append('\n');
            }
            catch (Exception e) { sb.Append("(the ship's place failed: ").Append(e.Message).Append(")\n"); }
            return sb.ToString();
        }

        void SendReport()
        {
            StopReportTyping();
            if (string.IsNullOrWhiteSpace(reportText))
            {
                reportNotice = Issue("issueNeedText", "Describe the problem first.");
                Show(Page.Report);
                return;
            }
            reportNotice = null;
            var now = DateTime.Now;
            var draft = new IssueReport.Draft { description = reportText, includeSave = reportIncludeSave, context = ReportContext() };
            try { if (reportShot != null) draft.screenshot = reportShot.EncodeToJPG(88); }
            catch (Exception e) { Debug.LogWarning("PauseMenu: the report's screenshot failed (" + e.Message + ")"); }
            var files = IssueReport.Collect(draft, now);
            reportPath = IssueReport.WriteZip(files, now);
            reportSent = false;
            reportTried = IssueReportUpload.Available;
            if (!reportTried) { Show(Page.ReportDone); return; }
            Show(Page.ReportBusy);
            IssueReportUpload.Send(IssueReport.Title(reportText), reportText.Trim(), files, reportShot,
                p => { if (reportProgress != null) reportProgress.text = string.Format(Issue("issueSending", "Sending the report... {0} %"), Mathf.RoundToInt(p * 100f)); },
                ok =>
                {
                    reportSent = ok;
                    if (IsOpen && page == Page.ReportBusy) Show(Page.ReportDone);
                    else if (ok) reportText = "";
                });
        }

        void BuildReportBusy()
        {
            reportProgress = Text(string.Format(Issue("issueSending", "Sending the report... {0} %"), 0));
        }

        void BuildReportDone()
        {
            // The zip as "IssueReports/GoF2_issue_....zip": the whole path is long and wraps badly; Show file opens its folder.
            string shown = reportPath != null ? Path.GetFileName(Path.GetDirectoryName(reportPath)) + "/" + Path.GetFileName(reportPath) : null;
            if (reportSent)
            {
                Text(Issue("issueSent", "Report sent. Thank you!"), "pause-heading");
                if (shown != null) Text(Issue("issueSavedAs", "A copy was saved as:") + "\n" + shown);
            }
            else if (shown != null)
            {
                if (reportTried) Text(Issue("issueNotSent", "The report could not be sent."), "pause-heading--red");
                Text(Issue("issueSavedOnly", "The report was saved as:") + "\n" + shown);
                if (!Application.isMobilePlatform)
                {
                    Text(Issue("issueAttachHint", "You can attach this file to a new issue on GitHub."));
                    string description = reportText, zip = Path.GetFileName(reportPath);
                    Item(Issue("issueOpenGitHub", "Open GitHub").UiCaps(), () => Application.OpenURL(IssueReport.IssueUrl(description, zip)));
                }
            }
            else Text(Issue("issueSaveFailed", "The report could not be saved."), "pause-heading--red");
            if (reportPath != null && PhotoMode.CanShowInFolder)
            {
                string path = reportPath;
                Item(Issue("issueShowFile", "Show file").UiCaps(), () => PhotoMode.ShowInFolder(path));
            }
            if (reportSent) reportText = "";   // the next report starts empty
        }
    }
}
