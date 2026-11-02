// GraphQuestState.cs
// Remake: what a save keeps of a single-player event graph run that lasts (Session.GraphQuests): a quest (the graph's
// Start node kind Quest, like a story chain) or a bar mission taken in a Space Lounge. EventRunner writes it when the run
// starts and at each checkpoint (the variables, objective, target and quiet orbits as they were there); loading a save
// starts the run again from that checkpoint (EventRunner.RestoreLocal). Plain data for JsonUtility.

using System;
using System.Collections.Generic;

namespace GoF2Remake.Data
{
    [Serializable]
    [Unity.Scripting.LifecycleManagement.NoAutoStaticsCleanup]
    public class GraphQuestState
    {
        public const int KindMission = 1, KindQuest = 2;

        /// <summary>A save was loaded with runs under way: LocalEvents (EventRunner.RestorePending) starts them again once a
        /// game scene is up.</summary>
        public static bool RestorePending;

        public string name = "";        // the graph's file name
        public int kind = KindQuest;
        public string title = "";       // the Start node's mission title (the Missions window)
        public string checkpoint = "";  // the last checkpoint passed ("": from the start)
        public List<string> varNames = new List<string>();
        public List<double> varValues = new List<double>();
        public string objective = "";
        public int target = -1;         // the station the story icon and Show on map point at (-1: none)
        public List<int> quietOrbits = new List<int>();
        public int station = -1;        // a bar mission: where it was taken
        public string offer = "";       // a bar mission: its offer (EventMissions' entry), for the Missions window
        public string once = "";        // a "mission once" bar mission: its name, done for good when the run succeeds
    }
}
