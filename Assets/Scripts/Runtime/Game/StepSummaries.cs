// StepSummaries.cs
// The Debug mission list's texts (remake-only): per story step a short one-line title and an optional subtitle
// (`summary`), from Resources/GoF2Data/step_summaries.json, which Reference/tools/campaign/build_step_summaries.py
// writes from its hand-written table.

using System;
using System.Collections.Generic;
using UnityEngine;

namespace GoF2Remake.Data
{
    [Unity.Scripting.LifecycleManagement.NoAutoStaticsCleanup]
    public static class StepSummaries
    {
        [Serializable]
        public class Entry
        {
            public int index;
            public string title;
            public string summary;
        }

        [Serializable]
        class File
        {
            public List<Entry> steps;
        }

        static Dictionary<int, Entry> byIndex;

        public static Entry Get(int index)
        {
            if (byIndex == null)
            {
                byIndex = new Dictionary<int, Entry>();
                var asset = GoF2Remake.Data.GameContent.Text("GoF2Data/step_summaries");
                var file = asset != null ? JsonUtility.FromJson<File>(asset) : null;
                if (file?.steps != null) foreach (var e in file.steps) byIndex[e.index] = e;
            }
            return byIndex.TryGetValue(index, out var entry) ? entry : null;
        }
    }
}
