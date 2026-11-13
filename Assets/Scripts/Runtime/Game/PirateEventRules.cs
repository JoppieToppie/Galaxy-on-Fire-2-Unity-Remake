// PirateEventRules.cs
// Where the remake's pirate events (outposts and bosses) may happen and how often (TrafficPlan builds them, GoF2.Space), here
// in GoF2.Game so the Pirate activity board (PirateReports) uses the same rules for its sightings.

using System.Collections.Generic;

namespace GoF2Remake.Data
{
    [Unity.Scripting.LifecycleManagement.NoAutoStaticsCleanup]
    public static class PirateEventRules
    {
        // Per security level (0 Dangerous, 1 Risky): the chance of an event per orbit entry, without and with the board.
        public static readonly int[] EventChance = { 15, 7 };
        public static readonly int[] UnreportedChance = { 5, 2 };

        static HashSet<int> storyStations;

        /// <summary>Every station a step of the three campaigns (story.json) sends the player to, the station the Void attack
        /// and step 59's convoy targets: the pirate events stay out of the storylines' orbits.</summary>
        public static bool InStoryline(int station)
        {
            if (storyStations == null)
            {
                storyStations = new HashSet<int>();
                foreach (var step in StoryTable.Steps) if (step != null && step.station >= 0) storyStations.Add(step.station);
            }
            return storyStations.Contains(station) || station == Session.VoidInvasionStation || Session.StoryTargets.Contains(station);
        }

        /// <summary>A pirate event may happen at 'station' (option on, rank 2+, not before campaign 0x10, not Mido in free
        /// play, not the special orbits, the storylines' stations, a pirate base's system or the Kaamo siege; a Dangerous or
        /// Risky system). Shared with the Pirate activity board (PirateReports).</summary>
        public static bool Allowed(Database db, int station, SystemData system, bool anyRank = false)
        {
            bool story = !Session.FreePlay;
            int cm = Session.CampaignMission;
            if (system == null || !Settings.PirateEvents || (story && cm < 0x10) || (!anyRank && Session.Rank < 2)) return false;
            if (PirateBases.SystemHasBase(db, system.index) || KaamoClub.SiegeAt(station)) return false;
            if (system.index == 15 && !story) return false;   // Mido: only once Keith has left it (free play keeps it quiet)
            if (station == 100 || station == 101 || station == 108 || station == 10 || (station >= 102 && station <= 104)) return false;
            if (system.index == 25 || system.index == 27 || system.index == 32 || system.index == 33 || Modding.ModWorld.PirateHideout(system.index)) return false;
            if (Modding.ModWorld.ModTrafficOnly(system.index)) return false;   // remake mods: a system with mods' ships only
            int sec = system.securityLevel;   // 0 Dangerous, 1 Risky, 2 Average, 3 Secure (texts 402-405)
            return sec >= 0 && sec < EventChance.Length && !InStoryline(station);
        }
    }
}
