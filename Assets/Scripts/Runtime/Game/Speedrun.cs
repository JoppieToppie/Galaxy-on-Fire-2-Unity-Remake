// Speedrun.cs
// Remake-only (players' request, docs/notes/speedrun.md): the speedrun timer's run. With Options > Gameplay "Speedrun
// timer" on (Settings.SpeedrunTimer), a single-player new game of a campaign starts a run (MainMenuNewGame, after
// Story.StartCampaign); every story step reached splits where its route says (Story.Advance -> Reached), the main game's
// ending ends it (StationMenu -> Reached(SpeedrunRoute.MainEndingStep)), and so does the last segment. Two clocks: real
// time, and game time = real time without the frames a scene loaded in (SpeedrunOverlay's Tick). A new game (any:
// Story.StartNewGame), the Reset button or quitting the game ends a run unfinished. The run goes on across game over's
// reload, the main menu and loaded saves: a split is only ever made once, the first time its step is reached.
// Every attempt is kept in the route's LiveSplit file in the runs folder (LiveSplitRun, GamePaths.Data/Speedruns), with
// its personal best, best segments and history; the splits compare against one of them (Settings.SpeedrunCompare: the
// personal best, the sum of best segments, the last attempt, an earlier finished one or another .lss of the same length
// in the folder, e.g. one from splits.io). LiveSplitLink mirrors the run to a LiveSplit on this computer.

using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using UnityEngine;

namespace GoF2Remake.Data
{
    [Unity.Scripting.LifecycleManagement.NoAutoStaticsCleanup]
    public static class Speedrun
    {
        public enum State { Off, Running, Finished }

        public static State Current { get; private set; }
        public static SpeedrunRoute Route { get; private set; }
        /// <summary>The run's clocks, ms.</summary>
        public static double RealMs { get; private set; }
        public static double GameMs { get; private set; }
        /// <summary>The cumulative split times per segment (empty = not reached or skipped).</summary>
        public static SplitTime[] Splits { get; private set; } = Array.Empty<SplitTime>();
        /// <summary>The segment being run (= Splits reached; Route.segments.Count once finished).</summary>
        public static int Segment { get; private set; }
        /// <summary>The finished run beat the personal best.</summary>
        public static bool NewPersonalBest { get; private set; }

        /// <summary>The comparison's split times in the chosen clock (null where it has none), and the best segments known
        /// before this run (for the gold splits).</summary>
        public static double?[] Comparison { get; private set; } = Array.Empty<double?>();
        public static double?[] BestSegments { get; private set; } = Array.Empty<double?>();
        public static string ComparisonName { get; private set; } = "";

        /// <summary>Remake (tests): the clock's now, UTC.</summary>
        public static Func<DateTime> Now = () => DateTime.UtcNow;
        /// <summary>The comparison's display names (set by the options catalog: Localization lives there too).</summary>
        public static Func<string, string> CompareLabel;

        static DateTime started;
        static string comparedKey;
        static bool comparedGameTime;

        public const string ComparePb = "pb", CompareBest = "best", CompareLast = "last", CompareNone = "none";

        [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.SubsystemRegistration)]
        static void ResetStatics()
        {
            Clear();
            Application.quitting -= OnQuit;
            Application.quitting += OnQuit;
        }

        static void OnQuit() { if (Current == State.Running) Reset(); }

        public static bool UsesGameTime => Settings.SpeedrunGameTime;
        public static double Time => UsesGameTime ? GameMs : RealMs;

        public static string Folder => Path.Combine(GamePaths.Data, "Speedruns");

        /// <summary>A new game of 'campaign' starts a run when the option is on (single player only).</summary>
        public static void NewGame(Campaign campaign, float difficulty, bool newGamePlus)
        {
            if (!Settings.SpeedrunTimer || NetSession.Active || NetSession.SessionGame) return;
            Begin(SpeedrunRoute.For(campaign, Settings.SpeedrunAllCampaigns, Settings.SpeedrunEverySplit, difficulty, newGamePlus));
        }

        public static void Begin(SpeedrunRoute route)
        {
            if (Current == State.Running) Reset();
            Route = route;
            Current = State.Running;
            RealMs = GameMs = 0;
            Splits = new SplitTime[route.segments.Count];
            Segment = 0;
            NewPersonalBest = false;
            started = Now();
            comparedKey = null;
            RefreshComparison();
            LiveSplitLink.Send("reset");
            LiveSplitLink.Send("starttimer");
            LiveSplitLink.Send("initgametime");
        }

        /// <summary>Runs the clocks on by 'ms' of real time; a frame a scene loaded in counts for real time only.</summary>
        public static void Tick(double ms, bool loading)
        {
            if (Current != State.Running || ms <= 0) return;
            RealMs += ms;
            if (!loading) GameMs += ms;
            else LiveSplitLink.Send("setgametime " + LiveSplitLink.GameTime(GameMs));   // LiveSplit's game time ran through the load
        }

        /// <summary>Story.Advance: the story reached 'step'. Splits every segment ending there or before (all but the last of
        /// them skipped: a loaded save further on), and finishes after the last.</summary>
        public static void Reached(int step)
        {
            if (Current != State.Running || Route == null) return;
            int last = Segment;
            while (last < Route.segments.Count && Route.segments[last].endStep <= step) last++;
            if (last == Segment) return;
            LiveSplitLink.Send("setgametime " + LiveSplitLink.GameTime(GameMs));
            for (; Segment < last; Segment++)
            {
                bool split = Segment == last - 1;
                if (split) Splits[Segment] = new SplitTime(RealMs, GameMs);
                LiveSplitLink.Send(split ? "split" : "skipsplit");
            }
            if (Segment >= Route.segments.Count) Finish();
        }

        static void Finish()
        {
            Current = State.Finished;
            NewPersonalBest = Record(true);
        }

        /// <summary>Ends a running run unfinished (kept as an attempt) or clears a finished one off the screen.</summary>
        public static void Reset()
        {
            if (Current == State.Running)
            {
                if (RealMs >= 1000) Record(false);
                LiveSplitLink.Send("reset");
            }
            Clear();
        }

        /// <summary>Story.StartNewGame (any new game: the menu's, the mission select's, a co-op or mod start): a run still
        /// going ends unfinished. The menu's new game begins the next one after it.</summary>
        public static void NewGameStarting()
        {
            if (Current == State.Running) Reset();
        }

        static void Clear()
        {
            Current = State.Off;
            Route = null;
            RealMs = GameMs = 0;
            Splits = Array.Empty<SplitTime>();
            Segment = 0;
            NewPersonalBest = false;
            Comparison = BestSegments = Array.Empty<double?>();
            ComparisonName = "";
            comparedKey = null;
        }

        // ---- the runs folder -----------------------------------------------------------------------------------------

        static string PathOf(SpeedrunRoute route) => Path.Combine(Folder, route.FileName);

        /// <summary>The route's file, or a new one (also when the file's segments no longer match the route's).</summary>
        public static LiveSplitRun Load(SpeedrunRoute route)
        {
            LiveSplitRun run = null;
            try
            {
                string path = PathOf(route);
                if (File.Exists(path)) run = LiveSplitRun.Parse(File.ReadAllText(path));
            }
            catch (Exception e) { Debug.LogWarning("Speedrun: can't read the runs file: " + e.Message); }
            if (run == null || run.segments.Count != route.segments.Count) return LiveSplitRun.NewFor(route);
            for (int i = 0; i < run.segments.Count; i++) run.segments[i].name = route.segments[i].name;
            return run;
        }

        /// <summary>Adds this run to its file. True = a new personal best.</summary>
        static bool Record(bool finished)
        {
            if (Route == null) return false;
            var run = Load(Route);
            bool pb = run.AddAttempt(Splits, finished ? Route.segments.Count : Segment, started, Now(), UsesGameTime);
            try
            {
                Directory.CreateDirectory(Folder);
                string path = PathOf(Route), temp = path + ".tmp";
                File.WriteAllText(temp, run.Write(), new System.Text.UTF8Encoding(false));
                if (File.Exists(path)) File.Delete(path);
                File.Move(temp, path);
            }
            catch (Exception e) { Debug.LogWarning("Speedrun: can't save the run: " + e.Message); }
            return pb;
        }

        /// <summary>The other .lss files in the runs folder with as many segments as the route (another player's run from
        /// splits.io, an older file), by file name.</summary>
        public static List<string> OtherFiles(SpeedrunRoute route)
        {
            var result = new List<string>();
            if (route == null) return result;
            try
            {
                if (!Directory.Exists(Folder)) return result;
                foreach (string path in Directory.GetFiles(Folder, "*.lss").OrderBy(p => p, StringComparer.OrdinalIgnoreCase))
                {
                    string name = Path.GetFileName(path);
                    if (string.Equals(name, route.FileName, StringComparison.OrdinalIgnoreCase)) continue;
                    var other = LiveSplitRun.Parse(File.ReadAllText(path));
                    if (other != null && other.segments.Count == route.segments.Count) result.Add(name);
                }
            }
            catch (Exception e) { Debug.LogWarning("Speedrun: can't list the runs folder: " + e.Message); }
            return result;
        }

        /// <summary>The comparisons the options offer for the current run (or the route the options would start next):
        /// key and label. The fixed four, then the finished attempts (newest first, at most 10), then the other files.</summary>
        public static List<(string key, string label)> Comparisons()
        {
            string L(string key) => CompareLabel?.Invoke(key) ?? key;
            var list = new List<(string, string)> { (ComparePb, L(ComparePb)), (CompareBest, L(CompareBest)), (CompareLast, L(CompareLast)), (CompareNone, L(CompareNone)) };
            var route = Route;
            if (route == null) return list;
            var run = Load(route);
            foreach (var a in run.Finished.Take(10))
                list.Add(("attempt:" + a.id, string.Format(L("attempt"), a.id, Format(a.time.Get(UsesGameTime) ?? a.time.real ?? 0),
                                                           a.ended?.ToLocalTime().ToString("d") ?? "")));
            foreach (string f in OtherFiles(route)) list.Add(("file:" + f, Path.GetFileNameWithoutExtension(f)));
            return list;
        }

        /// <summary>Reads the comparison again when the option or the clock changed (the overlay asks twice a second).</summary>
        public static void RefreshComparison()
        {
            if (Route == null) return;
            string key = Settings.SpeedrunCompare;
            bool gameTime = UsesGameTime;
            if (key == comparedKey && gameTime == comparedGameTime) return;
            comparedKey = key;
            comparedGameTime = gameTime;
            var run = Load(Route);
            BestSegments = run.segments.Select(s => s.bestSegment.Get(gameTime)).ToArray();
            double?[] comp;
            string name = Comparisons().FirstOrDefault(c => c.key == key).label;
            if (key == CompareBest) comp = run.SumOfBestSplits(gameTime);
            else if (key == CompareLast) comp = run.AttemptSplits(run.LastAttemptWithSplits(), gameTime);
            else if (key == CompareNone) comp = new double?[run.segments.Count];
            else if (key.StartsWith("attempt:") && int.TryParse(key.Substring(8), out int id)) comp = run.AttemptSplits(id, gameTime);
            else if (key.StartsWith("file:")) comp = OtherFileSplits(key.Substring(5), gameTime, run.segments.Count);
            else comp = run.PersonalBestSplits(gameTime);
            if (string.IsNullOrEmpty(name)) name = CompareLabel?.Invoke(ComparePb) ?? ComparePb;
            Comparison = comp;
            ComparisonName = name;
        }

        static double?[] OtherFileSplits(string file, bool gameTime, int count)
        {
            try
            {
                string path = Path.Combine(Folder, Path.GetFileName(file));
                var other = File.Exists(path) ? LiveSplitRun.Parse(File.ReadAllText(path)) : null;
                if (other != null && other.segments.Count == count)
                {
                    var pb = other.PersonalBestSplits(gameTime);
                    // A file timed in one clock only (most runs on splits.io are real time): its other clock stands in.
                    return pb.All(t => t == null) ? other.PersonalBestSplits(!gameTime) : pb;
                }
            }
            catch (Exception e) { Debug.LogWarning("Speedrun: can't read " + file + ": " + e.Message); }
            return new double?[count];
        }

        // ---- what the overlay shows ----------------------------------------------------------------------------------

        public static double? SplitAt(int i) => i >= 0 && i < Splits.Length ? Splits[i].Get(UsesGameTime) : null;
        public static double? CompareAt(int i) => i >= 0 && i < Comparison.Length ? Comparison[i] : null;

        /// <summary>A finished segment's time (null after a skip or without a time).</summary>
        public static double? SegmentTime(int i)
        {
            double? end = SplitAt(i);
            if (end == null) return null;
            if (i == 0) return end;
            double? start = SplitAt(i - 1);
            return start == null ? null : end - start;
        }

        /// <summary>The segment beat the best segment known before the run (a gold split).</summary>
        public static bool IsGold(int i)
        {
            double? t = SegmentTime(i), best = i < BestSegments.Length ? BestSegments[i] : null;
            return t != null && best != null && t < best;
        }

        /// <summary>The split's lead (-) or loss (+) on the comparison, ms; null without both.</summary>
        public static double? Delta(int i)
        {
            double? t = SplitAt(i), c = CompareAt(i);
            return t == null || c == null ? null : t - c;
        }

        /// <summary>The running segment's live delta, once the clock is past the comparison's split (LiveSplit shows it then)
        /// or ahead of the last split's delta.</summary>
        public static double? LiveDelta()
        {
            if (Current != State.Running) return null;
            double? c = CompareAt(Segment);
            if (c == null) return null;
            double d = Time - c.Value;
            double? previous = null;
            for (int i = Segment - 1; i >= 0 && previous == null; i--) previous = Delta(i);
            return d > 0 || previous != null && d > previous ? d : (double?)null;
        }

        /// <summary>"1:02:03.4", "2:03.4", "3.4".</summary>
        public static string Format(double ms, int decimals = 1)
        {
            var t = TimeSpan.FromMilliseconds(Math.Max(0, ms));
            string frac = decimals <= 0 ? "" : "." + ((int)(t.Milliseconds / Math.Pow(10, 3 - decimals))).ToString(new string('0', decimals));
            int hours = (int)t.TotalHours;
            if (hours > 0) return $"{hours}:{t.Minutes:00}:{t.Seconds:00}{frac}";
            if (t.Minutes > 0) return $"{t.Minutes}:{t.Seconds:00}{frac}";
            return $"{t.Seconds}{frac}";
        }

        /// <summary>A delta with its sign: "+1:02.3", "−4.5".</summary>
        public static string FormatDelta(double ms) => (ms < 0 ? "−" : "+") + Format(Math.Abs(ms));
    }
}
