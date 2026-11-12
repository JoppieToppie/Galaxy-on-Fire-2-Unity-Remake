// MainMenuDebug.cs
// Part of MainMenu: the Debug panel: the story step list with its search and the start at a step, the cheat toggles,
// the Debug button.

using System;
using System.Collections;
using System.Collections.Generic;
using GoF2Remake.Data;
using UnityEngine;
using UnityEngine.InputSystem;
using UnityEngine.InputSystem.Utilities;
using UnityEngine.Rendering;
using UnityEngine.Rendering.Universal;
using UnityEngine.SceneManagement;
using UnityEngine.UIElements;
using PointerType = UnityEngine.UIElements.PointerType;

namespace GoF2Remake.UI
{
    public partial class MainMenu
    {
        void BuildDebugPanel()
        {
            // A UI reload (PanelRenderer) rebuilds the tree: drop the old panel's elements.
            debugControls.Clear();
            missionRows.Clear();
            missionSections.Clear();
            var host = root.Q("panelHost");
            if (host == null) return;
            var panel = new VisualElement { name = "debugPanel" };
            panel.AddToClassList("panel");
            panel.AddToClassList("panel--wide");
            panel.AddToClassList("debug-panel");
            panel.usageHints = UsageHints.DynamicTransform;
            var title = new Label { name = "debugTitle" };
            title.AddToClassList("panel-title");
            title.AddToClassList("gof-semibold");
            panel.Add(title);
            var accent = new VisualElement();
            accent.AddToClassList("panel-accent");
            panel.Add(accent);
            var columns = new VisualElement();
            columns.AddToClassList("debug-columns");
            var left = new VisualElement();
            left.AddToClassList("debug-column");
            left.AddToClassList("debug-column--missions");
            // The cheats scroll on a short screen (the panel is sized to the screen, MainMenu.uss .debug-panel); the heading
            // and the note under them stay put (the note scrolled with them and showed cut off mid-line).
            var right = new VisualElement();
            right.AddToClassList("debug-column");
            right.AddToClassList("debug-column--cheats");
            var cheatList = new ScrollView(ScrollViewMode.Vertical) { horizontalScrollerVisibility = ScrollerVisibility.Hidden };
            cheatList.AddManipulator(new DragScroll(cheatList));
            cheatList.RegisterCallback<FocusInEvent>(e => { if (e.target is VisualElement v && v != cheatList) cheatList.ScrollTo(v); });   // follows the D-pad
            cheatList.AddToClassList("debug-cheat-list");
            columns.Add(left);
            columns.Add(right);
            panel.Add(columns);
            var back = new Button { name = "debugBack" };
            back.AddToClassList("menu-button");
            back.AddToClassList("back-button");
            back.AddToClassList("gof-semibold");
            back.clicked += Back;
            HookFocusSound(back);
            panel.Add(back);
            host.Add(panel);
            panels["debugPanel"] = panel;
            BuildMissionList(left);

            // The cheat toggles (Cheats; the actions are on the pause menu's / station's Debug page, in a running game).
            var heading = new Label { name = "debugCheatsTitle", pickingMode = PickingMode.Ignore };
            heading.AddToClassList("debug-heading");
            heading.AddToClassList("gof-semibold");
            right.Add(heading);
            right.Add(cheatList);
            foreach (var def in CheatsCatalog.Toggles())
            {
                var c = new OptionControl(def);
                c.Field.AddToClassList("option-row");
                c.Root.AddToClassList("debug-option");
                HookFocusSound(c.Field);
                c.Changed += () => Play(buttonRelease);
                cheatList.Add(c.Root);
                debugControls.Add(c);
            }
            var note = new Label { name = "debugNote", pickingMode = PickingMode.Ignore };
            note.AddToClassList("debug-note");
            right.Add(note);

            // Touch: five taps on the version text, each within 1 s of the last (its tap zone reaches well past the
            // small text, .footer-version).
            if (versionLabel != null)
            {
                versionLabel.pickingMode = PickingMode.Position;
                versionLabel.AddToClassList("footer-version");
                versionLabel.RegisterCallback<PointerDownEvent>(_ =>
                {
                    if (Time.unscaledTime - versionTapTime > 1f) versionTaps = 0;
                    versionTapTime = Time.unscaledTime;
                    if (++versionTaps >= 5) { versionTaps = 0; OpenDebug(); }
                });
            }
        }

        /// <summary>The main menu's Debug button: only with the debug tools on (Options > Gameplay, or opened once).</summary>
        void UpdateDebugButton()
        {
            debugButton?.EnableInClassList("menu-button--gone", !Cheats.Unlocked || !panels.ContainsKey("debugPanel"));
            UpdateColumnFit();
        }

        void OpenDebug()
        {
            if (!panels.ContainsKey("debugPanel") || openPanel == panels["debugPanel"]) return;
            if (dialog.ClassListContains("dialog-backdrop--shown")) return;
            Play(buttonRelease);
            Cheats.Unlocked = true;   // from now on the pause menu and the station's system menu have a Debug page
            UpdateDebugButton();
            foreach (var c in debugControls) c.Refresh();
            OpenPanel("debugPanel");
        }

        /// <summary>A search field and every story step, grouped by campaign: "index  title  station" over an optional
        /// subtitle (StepSummaries). A row starts that step (the difficulty panel first).</summary>
        void BuildMissionList(VisualElement parent)
        {
            var heading = new Label { name = "debugMissionsTitle", pickingMode = PickingMode.Ignore };
            heading.AddToClassList("debug-heading");
            heading.AddToClassList("gof-semibold");
            parent.Add(heading);
            missionFilter = new TextField { name = "debugFilter" };
            missionFilter.AddToClassList("debug-filter");
            missionFilter.textEdition.hidePlaceholderOnFocus = true;
            missionFilter.RegisterValueChangedCallback(e => FilterMissions(e.newValue));
            parent.Add(missionFilter);
            missionList = new ScrollView(ScrollViewMode.Vertical)
            {
                horizontalScrollerVisibility = ScrollerVisibility.Hidden,
                verticalScrollerVisibility = ScrollerVisibility.Hidden,
            };
            missionList.AddToClassList("debug-mission-list");
            new DragScroll(missionList);
            parent.Add(missionList);

            var db = Database.Load();
            Campaign? section = null;
            List<VisualElement> sectionRows = null;
            for (int i = 0; i <= Story.LastIndex; i++)
            {
                // Steps never current on their own (53 / 129 skipped, 46 / 107 passed through, 149 / 150
                // dialogue slots): the step that plays them stands for them.
                if (Story.MissionSelectStart(i) != i) continue;
                var campaign = CampaignOf(i);
                if (section != campaign)
                {
                    section = campaign;
                    var h = new Label(campaign switch
                    {
                        Campaign.Valkyrie => "VALKYRIE  ·  45-83",
                        Campaign.Supernova => "SUPERNOVA  ·  84-162",
                        _ => "GALAXY ON FIRE 2  ·  0-44",
                    }) { pickingMode = PickingMode.Ignore };
                    h.AddToClassList("debug-mission-section");
                    h.AddToClassList("gof-semibold");
                    missionList.Add(h);
                    sectionRows = new List<VisualElement>();
                    missionSections.Add((h, sectionRows));
                }
                var step = StoryTable.Step(i);
                var info = StepSummaries.Get(i);
                string station = step == null ? "" : step.station >= 0 ? db.Stations.Find(s => s.index == step.station)?.name ?? ""
                               : step.station == Session.VoidOrbit ? "Void" : "";
                // A one-line title over an optional subtitle (the objective texts repeat, e.g. the whole tutorial is
                // "I'm on my way to Var Hastra.", so they only stand in when a step has no title).
                string summary = info?.summary ?? "";
                string name = !string.IsNullOrEmpty(info?.title) ? info.title : Story.StepLabel(db, i);
                if (string.IsNullOrEmpty(name)) name = station;

                var row = new Button();
                row.AddToClassList("debug-mission");
                var top = new VisualElement { pickingMode = PickingMode.Ignore };
                top.AddToClassList("debug-mission-top");
                var num = new Label(i.ToString()) { pickingMode = PickingMode.Ignore };
                num.AddToClassList("debug-mission-index");
                num.AddToClassList("gof-semibold");
                top.Add(num);
                var label = new Label(name) { pickingMode = PickingMode.Ignore };
                label.AddToClassList("debug-mission-title");
                label.AddToClassList("gof-semibold");
                top.Add(label);
                if (!string.IsNullOrEmpty(station) && station != name)
                {
                    var where = new Label(station) { pickingMode = PickingMode.Ignore };
                    where.AddToClassList("debug-mission-station");
                    top.Add(where);
                }
                row.Add(top);
                if (!string.IsNullOrEmpty(summary))
                {
                    var sum = new Label(summary) { pickingMode = PickingMode.Ignore };
                    sum.AddToClassList("debug-mission-summary");
                    row.Add(sum);
                }
                int index = i;
                row.clicked += () => { Play(buttonRelease); StartAtStep(index); };
                HookFocusSound(row);
                missionList.Add(row);
                missionRows.Add((row, $"{i} {name} {station} {summary}".ToLowerInvariant()));
                sectionRows.Add(row);
            }
        }

        static Campaign CampaignOf(int index) =>
            index >= Story.Dlc1WonIndex ? Campaign.Supernova : index >= Story.GameWonIndex ? Campaign.Valkyrie : Campaign.GalaxyOnFire2;

        void StartAtStep(int index)
        {
            pendingStartIndex = index;
            pendingCampaign = CampaignOf(index);
            OpenPanel("difficultyPanel");
        }

        /// <summary>Rows whose index, title, station or summary contain every word typed (a number matches the index
        /// exactly); a campaign heading hides with all its rows.</summary>
        void FilterMissions(string query)
        {
            var words = (query ?? "").ToLowerInvariant().Split(new[] { ' ' }, StringSplitOptions.RemoveEmptyEntries);
            foreach (var (row, text) in missionRows)
            {
                bool match = true;
                foreach (var w in words)
                {
                    if (int.TryParse(w, out _)) { if (!text.StartsWith(w + " ")) { match = false; break; } }
                    else if (!text.Contains(w)) { match = false; break; }
                }
                row.style.display = match ? DisplayStyle.Flex : DisplayStyle.None;
            }
            foreach (var (heading, rows) in missionSections)
                heading.style.display = rows.Exists(r => r.style.display != DisplayStyle.None) ? DisplayStyle.Flex : DisplayStyle.None;
            missionList.scrollOffset = Vector2.zero;
        }
    }
}
