// SpeedrunTests.cs
// Feature: docs/notes/speedrun.md (the speedrun timer): SpeedrunRoute's segments per campaign and split style, Speedrun's
// splits (Story.Advance, skips, the end), its attempts in the route's LiveSplit file (personal best, best segments, the
// history an earlier run is rebuilt from, the comparisons), LiveSplitRun's .lss text (read back, LiveSplit's own parts
// kept, a file as LiveSplit writes it read).

using System;
using System.IO;
using System.Linq;
using GoF2Remake.Data;
using NUnit.Framework;

namespace GoF2Remake.Tests
{
    [Unity.Scripting.LifecycleManagement.NoAutoStaticsCleanup]
    public class SpeedrunTests : StoryAreaTestBase
    {
        DataUiTestKit.PrefsSnapshot prefs;
        Func<DateTime> savedNow;
        DateTime clock;

        [SetUp]
        public void Begin()
        {
            prefs = new DataUiTestKit.PrefsSnapshot();
            Settings.SpeedrunLiveSplit = false;
            Settings.SpeedrunGameTime = false;
            Settings.SpeedrunCompare = Speedrun.ComparePb;
            savedNow = Speedrun.Now;
            clock = new DateTime(2026, 11, 2, 12, 0, 0, DateTimeKind.Utc);
            Speedrun.Now = () => clock;
            Speedrun.Reset();
        }

        [TearDown]
        public void End()
        {
            Speedrun.Reset();
            Speedrun.Now = savedNow;
            prefs.Dispose();
        }

        static SpeedrunRoute Main(bool everyStep = false) => SpeedrunRoute.For(Campaign.GalaxyOnFire2, false, everyStep, Session.DifficultyNormal);

        [Test]
        public void ChaptersEndWhereTheirCampaignEnds()
        {
            var main = Main();
            Assert.AreEqual(10, main.segments.Count);
            Assert.AreEqual(SpeedrunRoute.MainEndingStep, main.segments[^1].endStep, "the main game ends with step 43's ending");
            Assert.AreEqual(Story.Dlc1WonIndex, SpeedrunRoute.For(Campaign.Valkyrie, false, false, 0.5f).segments[^1].endStep);
            Assert.AreEqual(Story.LastIndex, SpeedrunRoute.For(Campaign.Supernova, false, false, 0.5f).segments[^1].endStep);
            var all = SpeedrunRoute.For(Campaign.GalaxyOnFire2, true, false, 0.5f);
            Assert.AreEqual(25, all.segments.Count, "the three campaigns' chapters");
            Assert.AreEqual("All campaigns", all.Category);
            Assert.AreEqual("Supernova", SpeedrunRoute.For(Campaign.Supernova, true, false, 0.5f).Category, "nothing comes after Supernova");
        }

        [Test]
        public void EveryStepSplitsAtEachStepAdvanceReaches()
        {
            var main = Main(true);
            Assert.AreEqual(44, main.segments.Count, "steps 0..43");
            var valkyrie = SpeedrunRoute.For(Campaign.Valkyrie, false, true, 0.5f);
            Assert.AreEqual(Story.Dlc1WonIndex - Story.GameWonIndex - 1, valkyrie.segments.Count, "45..83 without 53");
            Assert.IsTrue(valkyrie.segments.All(s => s.endStep != 53), "Advance goes 52 -> 54");
            var supernova = SpeedrunRoute.For(Campaign.Supernova, false, true, 0.5f);
            Assert.IsTrue(supernova.segments.All(s => s.endStep != 129), "Advance goes 128 -> 130");
            foreach (var route in new[] { main, valkyrie, supernova, Main() })
                for (int i = 1; i < route.segments.Count; i++)
                    Assert.Greater(route.segments[i].endStep, route.segments[i - 1].endStep, route.FileName);
        }

        [Test]
        public void CategoryAndFileNameStayEnglish()
        {
            var route = SpeedrunRoute.For(Campaign.GalaxyOnFire2, false, true, Session.DifficultyExtreme, true);
            Assert.AreEqual("Main game (New Game+)", route.Category);
            Assert.AreEqual("Galaxy on Fire 2 - Main game (New Game+) - Extreme - Every step.lss", route.FileName);
        }

        void Run(double ms, int step, double loadMs = 0)
        {
            if (loadMs > 0) Speedrun.Tick(loadMs, true);
            Speedrun.Tick(ms, false);
            Speedrun.Reached(step);
        }

        [Test]
        public void StoryAdvanceSplitsAndTheEndingFinishes()
        {
            Speedrun.Begin(Main());
            Speedrun.Tick(60_000, false);
            Session.CampaignMission = 1;
            Story.Advance(Db);   // -> 2: the prologue chapter
            Assert.AreEqual(1, Speedrun.Segment);
            Assert.AreEqual(60_000, Speedrun.SplitAt(0));
            Story.Advance(Db);   // -> 3: inside the second chapter
            Assert.AreEqual(1, Speedrun.Segment, "no split inside a chapter");
            Session.CampaignMission = 1;
            Story.Advance(Db);   // a reloaded save reaches 2 again
            Assert.AreEqual(1, Speedrun.Segment, "a split is made once");
            Speedrun.Reached(SpeedrunRoute.MainEndingStep);
            Assert.AreEqual(Speedrun.State.Finished, Speedrun.Current);
            Assert.IsTrue(File.Exists(Path.Combine(Speedrun.Folder, Main().FileName)));
        }

        [Test]
        public void ALoadedSaveFurtherOnSkipsTheSplitsBetween()
        {
            Speedrun.Begin(Main());
            Run(1000, 9);   // two chapters at once: the first skipped
            Assert.IsNull(Speedrun.SplitAt(0));
            Assert.AreEqual(1000, Speedrun.SplitAt(1));
            Assert.AreEqual(2, Speedrun.Segment);
        }

        [Test]
        public void GameTimeLeavesOutLoading()
        {
            Speedrun.Begin(Main());
            Run(1000, 2, loadMs: 4000);
            Assert.AreEqual(5000, Speedrun.RealMs);
            Assert.AreEqual(1000, Speedrun.GameMs);
            Assert.AreEqual(new SplitTime(5000, 1000).real, Speedrun.Splits[0].real);
            Assert.AreEqual(1000, Speedrun.Splits[0].game);
        }

        double[] Finish(SpeedrunRoute route, params double[] segments)
        {
            Speedrun.Begin(route);
            for (int i = 0; i < route.segments.Count; i++) Run(i < segments.Length ? segments[i] : 1000, route.segments[i].endStep);
            Assert.AreEqual(Speedrun.State.Finished, Speedrun.Current);
            return Speedrun.Splits.Select(s => s.real.Value).ToArray();
        }

        [Test]
        public void PersonalBestBestSegmentsAndEarlierRuns()
        {
            var route = Main();
            Finish(route, 5000, 9000);                // run 1: 5 + 9 + 8 x 1
            Assert.IsTrue(Speedrun.NewPersonalBest, "the first finished run");
            Finish(route, 6000, 4000);                // run 2: 6 + 4 + 8 = 18 < 22
            Assert.IsTrue(Speedrun.NewPersonalBest);
            Finish(route, 4000, 9000, 2000);          // run 3: slower overall (4 + 9 + 2 + 7 = 22)
            Assert.IsFalse(Speedrun.NewPersonalBest);

            var file = Speedrun.Load(route);
            Assert.AreEqual(3, file.attempts.Count);
            Assert.AreEqual(18_000, file.PersonalBestSplits(false)[^1], "run 2");
            Assert.AreEqual(new double?[] { 4000, 8000, 9000 }, file.SumOfBestSplits(false).Take(3).ToArray(), "4 + 4 + 1");
            Assert.AreEqual(22_000, file.AttemptSplits(1, false)[^1]);
            Assert.AreEqual(3, file.LastAttemptWithSplits());

            Speedrun.Begin(route);
            Settings.SpeedrunCompare = "attempt:1";
            Speedrun.RefreshComparison();
            Run(4000, route.segments[0].endStep);
            Assert.AreEqual(-1000, Speedrun.Delta(0), "4 s against run 1's 5 s");
            Assert.IsFalse(Speedrun.IsGold(0), "ties run 3's best segment");
            Run(3000, route.segments[1].endStep);
            Assert.IsTrue(Speedrun.IsGold(1), "3 s beats the best 4 s");
        }

        [Test]
        public void AResetRunIsKeptUnfinished()
        {
            Speedrun.Begin(Main());
            Run(2000, 2);
            Speedrun.Tick(500, false);
            Speedrun.Reset();
            Assert.AreEqual(Speedrun.State.Off, Speedrun.Current);
            var file = Speedrun.Load(Main());
            Assert.AreEqual(1, file.attempts.Count);
            Assert.IsTrue(file.attempts[0].time.IsEmpty, "no final time");
            Assert.AreEqual(2000, file.segments[0].history[1].real);
            Assert.IsFalse(file.segments[1].history.ContainsKey(1), "never reached");
            Assert.AreEqual(1, file.LastAttemptWithSplits());

            Speedrun.Begin(Main());
            Story.StartNewGame();   // any new game ends the run
            Assert.AreEqual(Speedrun.State.Off, Speedrun.Current);
        }

        [Test]
        public void AnotherLssOfTheSameLengthIsOffered()
        {
            var route = Main();
            Finish(route);
            var other = LiveSplitRun.NewFor(route);
            other.categoryName = "Any%";
            other.AddAttempt(Enumerable.Range(1, route.segments.Count).Select(i => new SplitTime(i * 500.0, null)).ToArray(), route.segments.Count, clock, clock, false);
            File.WriteAllText(Path.Combine(Speedrun.Folder, "world record.lss"), other.Write());
            File.WriteAllText(Path.Combine(Speedrun.Folder, "other game.lss"), LiveSplitRun.NewFor(Main(true)).Write());   // 44 segments

            Speedrun.Begin(route);
            var keys = Speedrun.Comparisons().Select(c => c.key).ToList();
            CollectionAssert.Contains(keys, "file:world record.lss");
            CollectionAssert.DoesNotContain(keys, "file:other game.lss");
            CollectionAssert.Contains(keys, "attempt:1");
            Settings.SpeedrunCompare = "file:world record.lss";
            Settings.SpeedrunGameTime = true;   // the file has real time only: it stands in
            Speedrun.RefreshComparison();
            Assert.AreEqual(500, Speedrun.CompareAt(0));
        }

        [Test]
        public void LssRoundTripKeepsLiveSplitsOwnParts()
        {
            var route = Main();
            var run = LiveSplitRun.NewFor(route);
            var splits = route.segments.Select((s, i) => new SplitTime((i + 1) * 61_234.5, (i + 1) * 60_000.0)).ToArray();
            run.AddAttempt(splits, splits.Length, clock, clock.AddHours(1), false);
            string text = run.Write().Replace("<LayoutPath />", "<LayoutPath>my.lsl</LayoutPath>")
                                     .Replace("<AutoSplitterSettings />", "<AutoSplitterSettings><Keep>1</Keep></AutoSplitterSettings>");
            var back = LiveSplitRun.Parse(text);
            Assert.AreEqual("Main game", back.categoryName);
            Assert.AreEqual("Normal", back.variables["Difficulty"]);
            Assert.AreEqual(clock, back.attempts[0].started);
            Assert.AreEqual(Math.Round(splits[^1].real.Value), back.attempts[0].time.real);
            Assert.AreEqual(splits[^1].game, back.PersonalBestSplits(true)[^1]);
            Assert.AreEqual(60_000, back.segments[3].history[1].game);
            back.AddAttempt(splits, 1, clock, clock, false);
            string again = back.Write();
            StringAssert.Contains("<LayoutPath>my.lsl</LayoutPath>", again);
            StringAssert.Contains("<Keep>1</Keep>", again);
            StringAssert.Contains("<AttemptCount>2</AttemptCount>", again);
            StringAssert.StartsWith("<?xml version=\"1.0\" encoding=\"utf-8\"?>", again);
        }

        [Test]
        public void ReadsAFileAsLiveSplitWritesIt()
        {
            const string lss = @"<?xml version=""1.0"" encoding=""UTF-8""?>
<Run version=""1.7.0"">
  <GameIcon />
  <GameName>Galaxy on Fire 2</GameName>
  <CategoryName>Any%</CategoryName>
  <Metadata><Run id="""" /><Platform usesEmulator=""False"">PC</Platform><Region /><Variables /></Metadata>
  <Offset>00:00:00</Offset>
  <AttemptCount>2</AttemptCount>
  <AttemptHistory>
    <Attempt id=""1"" started=""10/31/2026 18:00:00"" isStartedSynced=""True"" ended=""10/31/2026 18:20:00"" isEndedSynced=""True"" />
    <Attempt id=""2"" started=""11/01/2026 18:00:00"" isStartedSynced=""True"" ended=""11/01/2026 19:30:05"" isEndedSynced=""True"">
      <RealTime>01:30:05.1230000</RealTime>
    </Attempt>
  </AttemptHistory>
  <Segments>
    <Segment>
      <Name>Prologue</Name><Icon />
      <SplitTimes><SplitTime name=""Personal Best""><RealTime>00:04:10.5000000</RealTime></SplitTime></SplitTimes>
      <BestSegmentTime><RealTime>00:04:00</RealTime></BestSegmentTime>
      <SegmentHistory><Time id=""1""><RealTime>00:04:00</RealTime></Time><Time id=""2""><RealTime>00:04:10.5000000</RealTime></Time></SegmentHistory>
    </Segment>
    <Segment>
      <Name>Var Hastra</Name><Icon />
      <SplitTimes><SplitTime name=""Personal Best""><RealTime>1.01:30:05.1230000</RealTime></SplitTime></SplitTimes>
      <BestSegmentTime />
      <SegmentHistory><Time id=""2"" /></SegmentHistory>
    </Segment>
  </Segments>
  <AutoSplitterSettings />
</Run>";
            var run = LiveSplitRun.Parse(lss);
            Assert.IsNotNull(run);
            Assert.AreEqual(2, run.attempts.Count);
            Assert.IsTrue(run.attempts[0].time.IsEmpty, "a reset attempt");
            Assert.AreEqual(250_500, run.PersonalBestSplits(false)[0]);
            Assert.AreEqual(TimeSpan.Parse("1.01:30:05.123").TotalMilliseconds, run.PersonalBestSplits(false)[1], "a day and more");
            Assert.IsNull(run.SumOfBestSplits(false)[1]);
            Assert.AreEqual(new double?[] { 250_500, null }, run.AttemptSplits(2, false), "skipped");
            Assert.AreEqual(new DateTime(2026, 11, 1, 19, 30, 5, DateTimeKind.Utc), run.attempts[1].ended);
            Assert.IsNull(LiveSplitRun.Parse("not xml"));
            Assert.IsNull(LiveSplitRun.Parse("<Layout />"));
        }

        [Test]
        public void TimesReadLikeATimer()
        {
            Assert.AreEqual("4.5", Speedrun.Format(4567));
            Assert.AreEqual("1:02.0", Speedrun.Format(62_000));
            Assert.AreEqual("1:00:00.0", Speedrun.Format(3_600_000));
            Assert.AreEqual("+1.2", Speedrun.FormatDelta(1250));
            Assert.AreEqual("−5.0", Speedrun.FormatDelta(-5000));
            Assert.AreEqual("1:02:03.45", LiveSplitLink.GameTime(3_723_450));
        }
    }
}
