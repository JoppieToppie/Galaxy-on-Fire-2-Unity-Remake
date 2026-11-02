// SpeedrunRoute.cs
// Remake-only (the speedrun timer, docs/notes/speedrun.md): what a run is timed over. A run starts with a new game of a
// campaign and ends where that campaign ends (or, "every campaign", at Supernova's end): the main game when step 43's
// ending starts (StationMenu: the credits; reached as step 44), Valkyrie on reaching step 84 (Story.Dlc1WonIndex),
// Supernova on reaching 162 (Story.LastIndex). Its segments end on reaching a story step (Story.Advance): one per step
// (StepSummaries' titles; 53 and 129 are never reached, Advance skips them) or per chapter (the table below, hand-made
// from the step list). Names, category and file name stay English: they go into LiveSplit files that are shared.

using System.Collections.Generic;

namespace GoF2Remake.Data
{
    /// <summary>A split: the segment ends when the story reaches 'endStep'.</summary>
    public sealed class SpeedrunSegment
    {
        public string name;
        public int endStep;
    }

    public sealed class SpeedrunRoute
    {
        public const string GameName = "Galaxy on Fire 2";

        public Campaign start;
        public bool allCampaigns, everyStep, newGamePlus;
        /// <summary>The English difficulty name (Easy / Normal / Hard / Extreme).</summary>
        public string difficulty;
        public readonly List<SpeedrunSegment> segments = new List<SpeedrunSegment>();

        /// <summary>LiveSplit's category: the campaigns the run covers, "(New Game+)" for a carried-over game.</summary>
        public string Category
        {
            get
            {
                string c = start switch
                {
                    Campaign.Valkyrie => allCampaigns ? "Valkyrie + Supernova" : "Valkyrie",
                    Campaign.Supernova => "Supernova",
                    _ => allCampaigns ? "All campaigns" : "Main game",
                };
                return newGamePlus ? c + " (New Game+)" : c;
            }
        }

        public string Splits => everyStep ? "Every step" : "Chapters";

        /// <summary>The run's LiveSplit file in the runs folder: one per category, difficulty and split style.</summary>
        public string FileName => Sanitize($"{GameName} - {Category} - {difficulty} - {Splits}.lss");

        static string Sanitize(string name)
        {
            foreach (char c in System.IO.Path.GetInvalidFileNameChars()) name = name.Replace(c, '_');
            return name;
        }

        /// <summary>The step a new game of the campaign starts at (Story.StartCampaign).</summary>
        public static int StartStep(Campaign c) => c switch
        {
            Campaign.Valkyrie => Story.GameWonIndex,
            Campaign.Supernova => Story.Dlc1WonIndex,
            _ => 0,
        };

        /// <summary>The step whose reaching ends the campaign's run (the main game's: its ending, counted as step 44).</summary>
        public static int EndStep(Campaign c) => c switch
        {
            Campaign.Valkyrie => Story.Dlc1WonIndex,
            Campaign.Supernova => Story.LastIndex,
            _ => MainEndingStep,
        };

        /// <summary>Step 43's ending (the credits) ends the main game's run: the step after it.</summary>
        public const int MainEndingStep = 44;

        // The chapters: (the step whose reaching ends it, its name), per campaign.
        static readonly (int end, string name)[] MainChapters =
        {
            (2, "Prologue"), (9, "Var Hastra"), (14, "Norris Bernard"), (18, "The Terran convoy"), (24, "Professor Boyle"),
            (31, "The Void"), (36, "The Khador Drive"), (40, "Errkt Uggut"), (43, "The Void mother ship"), (MainEndingStep, "The date with Carla"),
        };
        static readonly (int end, string name)[] ValkyrieChapters =
        {
            (55, "The K'Suukk"), (61, "The Liberators"), (66, "Khador kidnapped"), (73, "The Disruptor"),
            (80, "Escape from the Valkyrie"), (Story.Dlc1WonIndex, "The battle for Kothar"),
        };
        static readonly (int end, string name)[] SupernovaChapters =
        {
            (90, "The Ginoya supernova"), (96, "The evacuations"), (103, "Stealth fighters"), (109, "The reverse-matter bomb"),
            (123, "Moonsprocket"), (141, "The prisms"), (148, "Chromo Plasma"), (157, "Alice's deal"), (Story.LastIndex, "Trunt Harval"),
        };

        public static SpeedrunRoute For(Campaign start, bool allCampaigns, bool everyStep, float difficulty, bool newGamePlus = false)
        {
            var r = new SpeedrunRoute
            {
                start = start, allCampaigns = allCampaigns && start != Campaign.Supernova, everyStep = everyStep, newGamePlus = newGamePlus,
                difficulty = DifficultyName(difficulty),
            };
            int from = StartStep(start), to = r.allCampaigns ? Story.LastIndex : EndStep(start);
            if (everyStep)
            {
                for (int i = from; i < to; i++)
                {
                    if (i == 53 || i == 129) continue;   // Advance goes 52 -> 54, 128 -> 130
                    int next = i + 1 == 53 || i + 1 == 129 ? i + 2 : i + 1;
                    string title = StepSummaries.Get(i)?.title;
                    r.segments.Add(new SpeedrunSegment { name = string.IsNullOrEmpty(title) ? $"Step {i}" : $"{i}. {title}", endStep = next });
                }
            }
            else
            {
                foreach (var table in new[] { MainChapters, ValkyrieChapters, SupernovaChapters })
                    foreach (var (end, name) in table)
                        if (end > from && end <= to) r.segments.Add(new SpeedrunSegment { name = name, endStep = end });
            }
            return r;
        }

        /// <summary>English, for the shared files (Session.DifficultyName is the player's language).</summary>
        public static string DifficultyName(float d) => d > 1.25f ? "Extreme" : d > 0.75f ? "Hard" : d > 0.25f ? "Normal" : "Easy";
    }
}
