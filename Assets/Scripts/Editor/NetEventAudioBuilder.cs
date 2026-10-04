// NetEventAudioBuilder.cs
// GoF2 > Build > Event Audio: Resources/GoF2Net/EventAudio (NetEventAudio), the sounds and music events and admins play
// (/sound, /music; the event graphs' Play Sound / Play Music nodes), every wave of each one's original FMOD event
// (Reference/research/fmod_event_ids.txt, WeaponBuilder.LoadEventTable); the medal sound is the remake's own.

using System;
using System.Collections.Generic;
using System.IO;
using GoF2Remake.Multiplayer;
using UnityEditor;
using UnityEngine;

namespace GoF2Remake.EditorTools
{
    [Unity.Scripting.LifecycleManagement.NoAutoStaticsCleanup]
    public static class NetEventAudioBuilder
    {
        // FMOD event system ids by EventSound / EventMusic.
        static readonly Dictionary<EventSound, int> Sounds = new Dictionary<EventSound, int>
        {
            { EventSound.Alarm, 162 }, { EventSound.Warning, 35 }, { EventSound.Success, 36 }, { EventSound.Message, 125 },
            { EventSound.Info, 126 }, { EventSound.Click, 124 }, { EventSound.Explosion, 18 }, { EventSound.SmallExplosion, 20 },
            { EventSound.Jumpgate, 31 }, { EventSound.Khador, 32 }, { EventSound.Charge, 33 }, { EventSound.Lock, 26 },
            { EventSound.Boost, 38 }, { EventSound.Shield, 1115 }, { EventSound.TimeShift, 1119 },
        };

        static readonly Dictionary<EventMusic, int> Music = new Dictionary<EventMusic, int>
        {
            { EventMusic.Battle, 141 }, { EventMusic.BattleLow, 140 }, { EventMusic.BattleFull, 142 }, { EventMusic.VoidBattle, 136 },
            { EventMusic.VoidCalm, 145 }, { EventMusic.Boss, 151 }, { EventMusic.Specters, 149 }, { EventMusic.SpectersHigh, 150 },
            { EventMusic.Intro, 143 }, { EventMusic.Outro, 144 }, { EventMusic.HomeBase, 146 }, { EventMusic.Valkyrie, 147 },
            { EventMusic.Gamma, 148 }, { EventMusic.DeepScience, 152 }, { EventMusic.Terran, 134 }, { EventMusic.Vossk, 139 },
            { EventMusic.Nivelian, 138 }, { EventMusic.Midorian, 137 }, { EventMusic.Mothership, 155 },
        };

        const string AssetPath = "Assets/Resources/" + NetEventAudio.ResourcePath + ".asset";

        [MenuItem("GoF2/Build/Event Audio", priority = 207)]
        public static void Build()
        {
            var table = WeaponBuilder.LoadEventTable();
            var asset = AssetDatabase.LoadAssetAtPath<NetEventAudio>(AssetPath);
            if (asset == null)
            {
                Directory.CreateDirectory(Path.GetDirectoryName(AssetPath));
                asset = ScriptableObject.CreateInstance<NetEventAudio>();
                AssetDatabase.CreateAsset(asset, AssetPath);
            }
            var sounds = (EventSound[])Enum.GetValues(typeof(EventSound));
            asset.sounds = new NetEventAudio.Waves[sounds.Length];
            foreach (var s in sounds)
            {
                AudioClip[] clips = s == EventSound.Medal
                    ? new[] { AssetDatabase.LoadAssetAtPath<AudioClip>("Assets/Resources/GoF2Sfx/MedalToast.ogg") }
                    : Sounds.TryGetValue(s, out int id) ? WeaponBuilder.EventClips(table, id) : new AudioClip[0];
                asset.sounds[(int)s] = new NetEventAudio.Waves { clips = Array.FindAll(clips, c => c != null) };
                if (asset.sounds[(int)s].clips.Length == 0) Debug.LogWarning($"Event audio: no clip for the sound {s}");
            }
            var tracks = (EventMusic[])Enum.GetValues(typeof(EventMusic));
            asset.music = new AudioClip[tracks.Length];
            foreach (var m in tracks)
            {
                var clips = Music.TryGetValue(m, out int id) ? WeaponBuilder.EventClips(table, id) : new AudioClip[0];
                asset.music[(int)m] = clips.Length > 0 ? clips[0] : null;
                if (asset.music[(int)m] == null) Debug.LogWarning($"Event audio: no clip for the music {m}");
            }
            EditorUtility.SetDirty(asset);
            AssetDatabase.SaveAssets();
            Debug.Log($"Event audio: {sounds.Length} sounds, {tracks.Length} tracks -> {AssetPath}");
        }
    }
}
