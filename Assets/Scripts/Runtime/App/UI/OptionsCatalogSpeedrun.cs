// OptionsCatalogSpeedrun.cs
// OptionsCatalog: the speedrun timer's rows on the Gameplay page (docs/notes/speedrun.md; Speedrun, SpeedrunOverlay,
// LiveSplitLink). Every row but the toggle shows only while the timer is on.

using System;
using System.Collections.Generic;
using GoF2Remake.Data;
using UnityEngine;

namespace GoF2Remake.UI
{
    public static partial class OptionsCatalog
    {
        static List<(string key, string label)> speedrunComparisons;
        static float speedrunComparisonsAt = -10f;

        /// <summary>Speedrun.Comparisons, read again at most once a second (it lists the runs folder; the rows ask every refresh).</summary>
        static List<(string key, string label)> SpeedrunComparisons()
        {
            if (speedrunComparisons == null || Time.unscaledTime - speedrunComparisonsAt > 1f || Time.unscaledTime < speedrunComparisonsAt)
            {
                speedrunComparisons = Speedrun.Comparisons();
                speedrunComparisonsAt = Time.unscaledTime;
            }
            return speedrunComparisons;
        }

        /// <summary>The comparisons' names (Speedrun.CompareLabel).</summary>
        public static string SpeedrunCompareLabel(string key) => key switch
        {
            Speedrun.ComparePb => X("speedrunComparePb", "Personal best"),
            Speedrun.CompareBest => X("speedrunCompareBest", "Best segments"),
            Speedrun.CompareLast => X("speedrunCompareLast", "Last run"),
            Speedrun.CompareNone => X("speedrunCompareNone", "None"),
            "attempt" => X("speedrunCompareRun", "Run {0} · {1} · {2}"),
            _ => key,
        };

        /// <summary>AddGameplay's speedrun rows, after "Show story step".</summary>
        static void AddSpeedrun(List<OptionDef> list)
        {
            Func<bool> on = () => Settings.SpeedrunTimer;
            var timer = Toggle("speedrunTimer", OptionPage.Gameplay, () => X("speedrunTimer", "Speedrun timer"), () => Settings.SpeedrunTimer, v =>
            {
                Settings.SpeedrunTimer = v;
                if (!v) Speedrun.Reset();   // off ends the run
            });
            timer.description = () => X("speedrunTimerHelp", "A timer with splits from a new game to the end of its campaign. A new game starts the run.");
            list.Add(timer);

            var splits = Choice("speedrunSplits", OptionPage.Gameplay, () => X("speedrunSplits", "Splits"), true,
                () => new[] { X("speedrunSplitsChapters", "Story chapters"), X("speedrunSplitsSteps", "Every story step") },
                () => Settings.SpeedrunEverySplit ? 1 : 0, i => Settings.SpeedrunEverySplit = i == 1,
                () => X("speedrunSplitsHelp", "Where the timer splits. Takes effect from the next run."));
            splits.visible = on;
            list.Add(splits);

            var length = Choice("speedrunLength", OptionPage.Gameplay, () => X("speedrunLength", "Run length"), true,
                () => new[] { X("speedrunLengthCampaign", "The campaign started"), X("speedrunLengthAll", "On to the end of Supernova") },
                () => Settings.SpeedrunAllCampaigns ? 1 : 0, i => Settings.SpeedrunAllCampaigns = i == 1,
                () => X("speedrunLengthHelp", "Where a run ends: with its campaign, or on through the add-ons to the last story step."));
            length.visible = on;
            list.Add(length);

            var timing = Choice("speedrunTiming", OptionPage.Gameplay, () => X("speedrunTiming", "Timing"), true,
                () => new[] { X("speedrunTimingReal", "Real time"), X("speedrunTimingGame", "Without loading") },
                () => Settings.SpeedrunGameTime ? 1 : 0, i => Settings.SpeedrunGameTime = i == 1,
                () => X("speedrunTimingHelp", "The clock the splits and the personal best use. Both are saved."));
            timing.visible = on;
            list.Add(timing);

            var compare = Choice("speedrunCompare", OptionPage.Gameplay, () => X("speedrunCompare", "Compare against"), false,
                () => SpeedrunComparisons().ConvertAll(c => c.label).ToArray(),
                () => Math.Max(0, SpeedrunComparisons().FindIndex(c => c.key == Settings.SpeedrunCompare)),
                i =>
                {
                    var all = SpeedrunComparisons();
                    Settings.SpeedrunCompare = all[Mathf.Clamp(i, 0, all.Count - 1)].key;
                    Speedrun.RefreshComparison();
                },
                () => X("speedrunCompareHelp", "What the splits are measured against: your best run, your best segments, an earlier run or a LiveSplit file in the runs folder."));
            compare.visible = on;
            list.Add(compare);

            var position = Choice("speedrunPosition", OptionPage.Gameplay, () => X("speedrunPosition", "Timer position"), false,
                () => new[]
                {
                    X("fpsPosTopLeft", "Top left"), X("fpsPosTop", "Top"), X("fpsPosTopRight", "Top right"),
                    X("fpsPosLeft", "Left"), X("fpsPosRight", "Right"),
                    X("fpsPosBottomLeft", "Bottom left"), X("fpsPosBottom", "Bottom"), X("fpsPosBottomRight", "Bottom right"),
                },
                () => Settings.SpeedrunPosition, i => Settings.SpeedrunPosition = i,
                () => X("speedrunPositionHelp", "Where the speedrun timer sits."));
            position.visible = on;
            list.Add(position);

            // Desktop: LiveSplit runs on this computer (a phone has none to reach).
            if (!Application.isMobilePlatform)
            {
                var liveSplit = Toggle("speedrunLiveSplit", OptionPage.Gameplay, () => X("speedrunLiveSplit", "Connect to LiveSplit"),
                    () => Settings.SpeedrunLiveSplit, v => Settings.SpeedrunLiveSplit = v);
                liveSplit.description = () => X("speedrunLiveSplitHelp", "Sends the start, splits and resets to LiveSplit on this computer (Control > Start TCP Server, port 16834).")
                    + (Settings.SpeedrunLiveSplit ? " " + (LiveSplitLink.Connected ? X("speedrunLiveSplitOn", "Connected.") : X("speedrunLiveSplitOff", "Not connected.")) : "");
                liveSplit.visible = on;
                list.Add(liveSplit);
            }

            list.Add(new OptionDef
            {
                id = "speedrunReset", page = OptionPage.Gameplay, kind = OptionKind.Button,
                label = () => X("speedrunReset", "Reset the run"),
                description = () => X("speedrunResetHelp", "Ends the run now and keeps it as an unfinished attempt."),
                action = Speedrun.Reset,
                visible = on,
                enabled = () => Speedrun.Current != Speedrun.State.Off,
            });

            if (!Application.isMobilePlatform)
                list.Add(new OptionDef
                {
                    id = "speedrunFolder", page = OptionPage.Gameplay, kind = OptionKind.Button,
                    label = () => X("speedrunFolder", "Open the runs folder"),
                    description = () => X("speedrunFolderHelp", "Runs are saved as LiveSplit splits files (.lss) for LiveSplit and splits.io. Put other .lss files here to compare against them."),
                    action = () =>
                    {
                        System.IO.Directory.CreateDirectory(Speedrun.Folder);
                        Application.OpenURL("file:///" + Speedrun.Folder.Replace('\\', '/'));
                    },
                    visible = on,
                });
        }
    }
}
