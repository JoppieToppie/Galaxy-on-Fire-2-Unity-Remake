// EventAudio.cs
// Remake events: the sounds and music an event (or an admin's /sound and /music) can play on players' games,
// Resources/GoF2Events/EventAudio (GoF2 > Build > Event Audio, EventAudioBuilder: each entry from the original's FMOD event,
// Reference/research/fmod_event_ids.txt). Indexed by EventSound / EventMusic; a sound with several waves picks one at random.

using System;
using UnityEngine;
using GoF2Remake.Multiplayer;

namespace GoF2Remake.Events
{
    /// <summary>The event sounds (FMOD event ids in EventAudioBuilder).</summary>
    public enum EventSound { Alarm, Warning, Success, Message, Info, Click, Explosion, SmallExplosion, Jumpgate, Khador, Charge, Lock, Boost, Shield, TimeShift, Medal }

    /// <summary>The event music tracks (FMOD event ids in EventAudioBuilder).</summary>
    public enum EventMusic { Battle, BattleLow, BattleFull, VoidBattle, VoidCalm, Boss, Specters, SpectersHigh, Intro, Outro, HomeBase, Valkyrie, Gamma, DeepScience, Terran, Vossk, Nivelian, Midorian, Mothership }

    [CreateAssetMenu(menuName = "GoF2/Event Audio")]
    [Unity.Scripting.LifecycleManagement.NoAutoStaticsCleanup]
    public class EventAudio : ScriptableObject
    {
        [Serializable]
        public class Waves
        {
            public AudioClip[] clips = new AudioClip[0];
        }

        public const string ResourcePath = "GoF2Events/EventAudio";

        /// <summary>By EventSound.</summary>
        public Waves[] sounds = new Waves[0];
        /// <summary>By EventMusic.</summary>
        public AudioClip[] music = new AudioClip[0];

        static EventAudio cached;

        public static EventAudio Load() => cached != null ? cached : cached = GoF2Remake.Data.OriginalRefs.Load<EventAudio>(ResourcePath);

        public AudioClip Sound(int index)
        {
            if (index < 0 || index >= sounds.Length || sounds[index] == null || sounds[index].clips.Length == 0) return null;
            var c = sounds[index].clips;
            return c[UnityEngine.Random.Range(0, c.Length)];
        }

        public AudioClip Music(int index) => index >= 0 && index < music.Length ? music[index] : null;

        /// <summary>A sound / track by its name in any case ("small explosion", "smallexplosion", "small_explosion").</summary>
        public static bool TryParse<T>(string word, out T value) where T : struct, Enum
        {
            string w = (word ?? "").Replace(" ", "").Replace("_", "").Replace("-", "");
            foreach (T v in Enum.GetValues(typeof(T)))
                if (string.Equals(v.ToString(), w, StringComparison.OrdinalIgnoreCase)) { value = v; return true; }
            value = default;
            return false;
        }

        public static string Names<T>() where T : struct, Enum => string.Join(", ", Array.ConvertAll((T[])Enum.GetValues(typeof(T)), v => v.ToString().ToLowerInvariant()));
    }
}
