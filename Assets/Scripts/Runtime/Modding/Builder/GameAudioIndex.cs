// GameAudioIndex.cs
// The game's sound effects and music tracks by name (Resources/GoF2ModBuilder/GameAudioIndex, GoF2 > Build > Mod Builder
// Audio Previews): the Mod Builder's Audio pickers play a sound or track before it is replaced (the user's request,
// 2026-10-22: the names alone didn't tell which one is wanted). The clips are only reachable through an asset at run time;
// nearly all of them ship anyway (the levels, CombatAssets, the weapon fx hold them). The voice lines come from StoryAssets.

using System;
using System.Collections.Generic;
using UnityEngine;

namespace GoF2Remake.Modding
{
    [Unity.Scripting.LifecycleManagement.NoAutoStaticsCleanup]
    public class GameAudioIndex : ScriptableObject
    {
        public const string ResourcePath = "GoF2ModBuilder/GameAudioIndex";

        [Tooltip("The clips' names (the file names in Assets/Audio), in step with 'clips'.")]
        public List<string> names = new List<string>();
        public List<AudioClip> clips = new List<AudioClip>();

        Dictionary<string, int> byName;   // a sound's name -> its index in clips
        static GameAudioIndex instance;

        [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.SubsystemRegistration)]
        static void ResetStatics() => instance = null;

        public static GameAudioIndex Load() => instance != null ? instance : instance = GoF2Remake.Data.OriginalRefs.Load<GameAudioIndex>(ResourcePath);

        /// <summary>The game's sound effect or music track of that name, or null.</summary>
        public static AudioClip Clip(string name)
        {
            var index = Load();
            if (index == null || string.IsNullOrEmpty(name)) return null;
            if (index.byName == null)
            {
                index.byName = new Dictionary<string, int>(StringComparer.OrdinalIgnoreCase);
                for (int i = 0; i < index.names.Count && i < index.clips.Count; i++)
                    index.byName[index.names[i]] = i;
            }
            // (a build without the originals: the clip from the converted copy the first time it is asked for, OriginalRefs)
            return index.byName.TryGetValue(name, out int at) ? GoF2Remake.Data.OriginalRefs.Entry(index, nameof(clips), index.clips, at) : null;
        }
    }
}
