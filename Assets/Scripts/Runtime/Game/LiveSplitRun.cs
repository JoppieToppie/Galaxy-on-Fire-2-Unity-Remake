// LiveSplitRun.cs
// Remake-only (the speedrun timer, docs/notes/speedrun.md): a LiveSplit splits file (.lss, the XML LiveSplit 1.7+ saves,
// which splits.io and other timers read too) as the speedrun timer keeps its runs: the attempt history, each segment's
// personal best split time, best segment and per-attempt segment times, in real time and game time (LiveSplit's
// "RealTime" / "GameTime"; the remake's game time = without loading). Read keeps the document, and Save writes only the
// parts the timer owns into it, so what LiveSplit added to a file it opened (its layout, other comparisons, an auto
// splitter's settings) survives. Times are TimeSpan text ("01:02:03.4560000"), attempt dates LiveSplit's
// "MM/dd/yyyy HH:mm:ss" in UTC.

using System;
using System.Collections.Generic;
using System.Globalization;
using System.Linq;
using System.Xml.Linq;

namespace GoF2Remake.Data
{
    /// <summary>A time in milliseconds for each timing method (null = none).</summary>
    public struct SplitTime
    {
        public double? real, game;
        public SplitTime(double? real, double? game) { this.real = real; this.game = game; }
        public double? Get(bool gameTime) => gameTime ? game : real;
        public bool IsEmpty => real == null && game == null;
    }

    public sealed class LiveSplitRun
    {
        public sealed class Attempt
        {
            public int id;
            public DateTime? started, ended;
            /// <summary>The finished run's time; empty for an attempt that was reset.</summary>
            public SplitTime time;
        }

        public sealed class Segment
        {
            public string name;
            public SplitTime personalBest, bestSegment;
            /// <summary>The segment's time in each attempt that reached it, by attempt id (empty = skipped).</summary>
            public readonly SortedDictionary<int, SplitTime> history = new SortedDictionary<int, SplitTime>();
        }

        public string gameName = SpeedrunRoute.GameName, categoryName = "";
        public readonly Dictionary<string, string> variables = new Dictionary<string, string>();
        public readonly List<Attempt> attempts = new List<Attempt>();
        public readonly List<Segment> segments = new List<Segment>();
        XDocument doc;

        const string DateFormat = "MM/dd/yyyy HH:mm:ss";
        const string PersonalBest = "Personal Best";

        public static LiveSplitRun NewFor(SpeedrunRoute route)
        {
            var run = new LiveSplitRun { categoryName = route.Category };
            run.variables["Difficulty"] = route.difficulty;
            run.variables["Splits"] = route.Splits;
            foreach (var s in route.segments) run.segments.Add(new Segment { name = s.name });
            return run;
        }

        public int NextAttemptId => attempts.Count == 0 ? 1 : attempts.Max(a => a.id) + 1;

        // ---- comparisons ---------------------------------------------------------------------------------------------

        /// <summary>The personal best's split times (cumulative), per segment; null where it has none.</summary>
        public double?[] PersonalBestSplits(bool gameTime) => segments.Select(s => s.personalBest.Get(gameTime)).ToArray();

        /// <summary>The sum of best segments as split times: each the best segments so far added up (null from the first
        /// segment without one).</summary>
        public double?[] SumOfBestSplits(bool gameTime)
        {
            var result = new double?[segments.Count];
            double total = 0;
            for (int i = 0; i < segments.Count; i++)
            {
                double? b = segments[i].bestSegment.Get(gameTime);
                if (b == null) break;
                total += b.Value;
                result[i] = total;
            }
            return result;
        }

        /// <summary>An attempt's split times rebuilt from the segment history (LiveSplit's): a segment after a skipped one
        /// covers both, so the sums run on; null where the attempt skipped or never got.</summary>
        public double?[] AttemptSplits(int id, bool gameTime)
        {
            var result = new double?[segments.Count];
            double total = 0;
            for (int i = 0; i < segments.Count; i++)
            {
                if (!segments[i].history.TryGetValue(id, out var t)) break;
                double? v = t.Get(gameTime);
                if (v == null) continue;
                total += v.Value;
                result[i] = total;
            }
            return result;
        }

        /// <summary>The newest attempt that reached at least one split (-1 = none).</summary>
        public int LastAttemptWithSplits()
        {
            foreach (var a in attempts.OrderByDescending(a => a.id))
                if (segments.Count > 0 && segments[0].history.ContainsKey(a.id)) return a.id;
            return -1;
        }

        /// <summary>The finished attempts, newest first.</summary>
        public IEnumerable<Attempt> Finished => attempts.Where(a => !a.time.IsEmpty).OrderByDescending(a => a.id);

        // ---- recording -----------------------------------------------------------------------------------------------

        /// <summary>Adds an attempt: 'splits' its cumulative split times per segment (empty = skipped, the array cut where it
        /// was reset), finished when the last segment has a time. Updates the segment history, the best segments (a
        /// segment after a skip doesn't count, as in LiveSplit) and the personal best (by 'gameTime''s clock). True when it
        /// is a new personal best.</summary>
        public bool AddAttempt(SplitTime[] splits, int reached, DateTime started, DateTime ended, bool gameTime)
        {
            int id = NextAttemptId;
            bool finished = reached == segments.Count && reached > 0 && !splits[reached - 1].IsEmpty;
            attempts.Add(new Attempt { id = id, started = started, ended = ended, time = finished ? splits[reached - 1] : default });
            SplitTime previous = new SplitTime(0, 0);
            bool previousSkipped = false;
            for (int i = 0; i < reached && i < segments.Count; i++)
            {
                var s = splits[i];
                if (s.IsEmpty) { segments[i].history[id] = default; previousSkipped = true; continue; }
                var seg = new SplitTime(s.real - previous.real, s.game - previous.game);
                segments[i].history[id] = seg;
                if (!previousSkipped)
                {
                    var best = segments[i].bestSegment;
                    if (seg.real != null && (best.real == null || seg.real < best.real)) best.real = seg.real;
                    if (seg.game != null && (best.game == null || seg.game < best.game)) best.game = seg.game;
                    segments[i].bestSegment = best;
                }
                previous = s;
                previousSkipped = false;
            }
            if (!finished) return false;
            double? mine = splits[reached - 1].Get(gameTime), pb = segments[^1].personalBest.Get(gameTime);
            if (mine == null || pb != null && pb <= mine) return false;
            for (int i = 0; i < segments.Count; i++) segments[i].personalBest = splits[i];
            return true;
        }

        // ---- reading -------------------------------------------------------------------------------------------------

        /// <summary>Reads a .lss text; null when it isn't one.</summary>
        public static LiveSplitRun Parse(string xml)
        {
            XDocument d;
            try { d = XDocument.Parse(xml); }   // whitespace dropped: Save indents the whole file again
            catch (Exception) { return null; }
            var root = d.Root;
            if (root == null || root.Name.LocalName != "Run") return null;
            var run = new LiveSplitRun { doc = d };
            run.gameName = (string)root.Element("GameName") ?? "";
            run.categoryName = (string)root.Element("CategoryName") ?? "";
            var vars = root.Element("Metadata")?.Element("Variables");
            if (vars != null)
                foreach (var v in vars.Elements("Variable"))
                    if (v.Attribute("name") != null) run.variables[(string)v.Attribute("name")] = v.Value;
            var history = root.Element("AttemptHistory");
            if (history != null)
                foreach (var a in history.Elements("Attempt"))
                    run.attempts.Add(new Attempt
                    {
                        id = (int?)Int(a.Attribute("id")?.Value) ?? 0,
                        started = Date(a.Attribute("started")?.Value),
                        ended = Date(a.Attribute("ended")?.Value),
                        time = Times(a),
                    });
            var segs = root.Element("Segments");
            if (segs != null)
                foreach (var s in segs.Elements("Segment"))
                {
                    var seg = new Segment { name = (string)s.Element("Name") ?? "" };
                    var pb = s.Element("SplitTimes")?.Elements("SplitTime").FirstOrDefault(t => (string)t.Attribute("name") == PersonalBest);
                    if (pb != null) seg.personalBest = Times(pb);
                    if (s.Element("BestSegmentTime") != null) seg.bestSegment = Times(s.Element("BestSegmentTime"));
                    var sh = s.Element("SegmentHistory");
                    if (sh != null)
                        foreach (var t in sh.Elements("Time"))
                        {
                            int? id = Int(t.Attribute("id")?.Value);
                            if (id != null) seg.history[id.Value] = Times(t);
                        }
                    run.segments.Add(seg);
                }
            return run;
        }

        static int? Int(string s) => int.TryParse(s, NumberStyles.Integer, CultureInfo.InvariantCulture, out int v) ? v : (int?)null;

        static DateTime? Date(string s) =>
            DateTime.TryParseExact(s, DateFormat, CultureInfo.InvariantCulture, DateTimeStyles.AssumeUniversal | DateTimeStyles.AdjustToUniversal, out var d)
                ? d : (DateTime?)null;

        static SplitTime Times(XElement e) => new SplitTime(Time((string)e.Element("RealTime")), Time((string)e.Element("GameTime")));

        static double? Time(string s) =>
            !string.IsNullOrWhiteSpace(s) && TimeSpan.TryParse(s.Trim(), CultureInfo.InvariantCulture, out var t) ? t.TotalMilliseconds : (double?)null;

        // ---- writing -------------------------------------------------------------------------------------------------

        static string Text(double ms) => TimeSpan.FromMilliseconds(Math.Round(ms)).ToString("c", CultureInfo.InvariantCulture);

        static void AddTimes(XElement e, SplitTime t)
        {
            if (t.real != null) e.Add(new XElement("RealTime", Text(t.real.Value)));
            if (t.game != null) e.Add(new XElement("GameTime", Text(t.game.Value)));
        }

        /// <summary>The file's text: the document read (or a new one), its parts the timer keeps written anew.</summary>
        public string Write()
        {
            doc ??= new XDocument(new XDeclaration("1.0", "UTF-8", null), new XElement("Run", new XAttribute("version", "1.7.0"),
                new XElement("GameIcon"), new XElement("GameName"), new XElement("CategoryName"), new XElement("LayoutPath"),
                new XElement("Metadata", new XElement("Run", new XAttribute("id", "")),
                    new XElement("Platform", new XAttribute("usesEmulator", "False"), "PC"), new XElement("Region"), new XElement("Variables")),
                new XElement("Offset", "00:00:00"), new XElement("AttemptCount"), new XElement("AttemptHistory"), new XElement("Segments"),
                new XElement("AutoSplitterSettings")));
            var root = doc.Root;
            Child(root, "GameName").Value = gameName ?? "";
            Child(root, "CategoryName").Value = categoryName ?? "";
            var metadata = Child(root, "Metadata");
            var vars = Child(metadata, "Variables");
            foreach (var kv in variables)
            {
                var v = vars.Elements("Variable").FirstOrDefault(e => (string)e.Attribute("name") == kv.Key);
                if (v == null) vars.Add(v = new XElement("Variable", new XAttribute("name", kv.Key)));
                v.Value = kv.Value ?? "";
            }
            Child(root, "AttemptCount").Value = attempts.Count.ToString(CultureInfo.InvariantCulture);
            var history = Child(root, "AttemptHistory");
            history.RemoveAll();
            foreach (var a in attempts.OrderBy(a => a.id))
            {
                var e = new XElement("Attempt", new XAttribute("id", a.id));
                if (a.started != null) { e.Add(new XAttribute("started", a.started.Value.ToString(DateFormat, CultureInfo.InvariantCulture))); e.Add(new XAttribute("isStartedSynced", "False")); }
                if (a.ended != null) { e.Add(new XAttribute("ended", a.ended.Value.ToString(DateFormat, CultureInfo.InvariantCulture))); e.Add(new XAttribute("isEndedSynced", "False")); }
                AddTimes(e, a.time);
                history.Add(e);
            }
            var segs = Child(root, "Segments");
            var existing = segs.Elements("Segment").ToList();
            if (existing.Count != segments.Count) { segs.RemoveAll(); existing.Clear(); }
            for (int i = 0; i < segments.Count; i++)
            {
                var s = segments[i];
                XElement e;
                if (i < existing.Count) e = existing[i];
                else segs.Add(e = new XElement("Segment", new XElement("Name"), new XElement("Icon"), new XElement("SplitTimes"),
                                                   new XElement("BestSegmentTime"), new XElement("SegmentHistory")));
                Child(e, "Name").Value = s.name ?? "";
                var splitTimes = Child(e, "SplitTimes");
                var pb = splitTimes.Elements("SplitTime").FirstOrDefault(t => (string)t.Attribute("name") == PersonalBest);
                if (pb == null) splitTimes.AddFirst(pb = new XElement("SplitTime", new XAttribute("name", PersonalBest)));
                pb.RemoveNodes();
                AddTimes(pb, s.personalBest);
                var best = Child(e, "BestSegmentTime");
                best.RemoveNodes();
                AddTimes(best, s.bestSegment);
                var sh = Child(e, "SegmentHistory");
                sh.RemoveNodes();
                foreach (var kv in s.history)
                {
                    var t = new XElement("Time", new XAttribute("id", kv.Key));
                    AddTimes(t, kv.Value);
                    sh.Add(t);
                }
            }
            var sw = new Utf8Writer();
            doc.Save(sw);
            return sw.ToString();
        }

        static XElement Child(XElement parent, string name)
        {
            var e = parent.Element(name);
            if (e == null) parent.Add(e = new XElement(name));
            return e;
        }

        /// <summary>A StringWriter that declares UTF-8 (the file is written as UTF-8), not UTF-16.</summary>
        sealed class Utf8Writer : System.IO.StringWriter
        {
            public Utf8Writer() : base(CultureInfo.InvariantCulture) { }
            public override System.Text.Encoding Encoding => new System.Text.UTF8Encoding(false);
        }
    }
}
