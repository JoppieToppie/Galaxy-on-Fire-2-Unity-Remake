// Sfx.cs
// One-shot sound effects in space. Distances are kilometres, so instead of Unity's 3D rolloff the volume falls off
// linearly with the distance to the camera (the original plays FMOD events at the ship position when 3D sound is on).
// Remake: a fixed set of voices in the active scene (gone with it, as the old one-off objects were): a free one, else the
// quietest playing one when the new sound is louder (like ShotVoices' cap). Each sound was a new GameObject with an
// AudioSource, destroyed after the clip, and a fleet battle's hits and deaths took as many voices as they liked.

using GoF2Remake.Data;
using UnityEngine;

namespace GoF2Remake.Flight
{
    [Unity.Scripting.LifecycleManagement.NoAutoStaticsCleanup]
    public static class Sfx
    {
        public const float AudibleMeters = 3000f;   // silent beyond (60000 game units)
        /// <summary>An FMOD event's own volume (the FEV's LGCY data: lasers 0.13-0.29, engines 0.06-0.1, boosters 0.2) times
        /// this is the remake's source volume, matching the levels the remake's other sounds already play at.</summary>
        public const float EventGain = 4f;

        const int Voices = 24;
        static AudioSource[] voices;
        static GameObject holder;   // null again once its scene is gone

        [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.SubsystemRegistration)]
        static void ResetStatics() { voices = null; holder = null; }

        /// <summary>Modified: where a sound at 'position' sits left / right of the camera, as a stereo pan (-1..1): the original
        /// plays these FMOD events in 3D at the ship, so an enemy's guns and explosions are heard from its side; the remake's
        /// 2D voices were centred. Never fully one-sided (PanAmount), and centred straight ahead, behind or at the camera.</summary>
        public static float Pan(Vector3 position)
        {
            var cam = Camera.main;
            if (cam == null) return 0f;
            return PanLocal(cam.transform.InverseTransformPoint(position));
        }

        /// <summary>Pan's rule for a point in the camera's own space (x right, z ahead; metres).</summary>
        public static float PanLocal(Vector3 local)
        {
            float len = local.magnitude;
            if (len < 1f) return 0f;
            return Mathf.Clamp(local.x / len, -1f, 1f) * PanAmount;
        }

        /// <summary>How far to one side a sound fully left / right of the camera is panned.</summary>
        public const float PanAmount = 0.85f;

        public static void PlayAt(AudioClip clip, Vector3 position, float volume = 1f) => Play(clip, position, volume);

        /// <summary>Modified: a one-shot that stays on a moving object (the original's 3D event on its geometry: 92's
        /// freighter and 102's carrier jumping away): its volume and pan follow 'follow' while it plays (SfxFollow).</summary>
        public static void PlayOn(AudioClip clip, Transform follow, float volume = 1f)
        {
            if (follow == null) return;
            var src = Play(clip, follow.position, volume);
            if (src == null) return;
            var f = src.GetComponent<SfxFollow>();
            if (f == null) f = src.gameObject.AddComponent<SfxFollow>();
            f.Begin(src, follow, volume);
        }

        /// <summary>The voice's volume for a sound of 'volume' at 'position' (the linear fall-off to AudibleMeters).</summary>
        public static float VolumeAt(Vector3 position, float volume)
        {
            var cam = Camera.main;
            float d = cam != null ? Vector3.Distance(cam.transform.position, position) : 0f;
            return volume * Mathf.Clamp01(1f - d / AudibleMeters) * Settings.SfxVolume;
        }

        static AudioSource Play(AudioClip clip, Vector3 position, float volume)
        {
            if (clip == null) return null;
            float v = VolumeAt(position, volume);
            if (v <= 0.01f) return null;
            var src = Voice(v);
            if (src == null) return null;   // every voice busy with a louder sound
            var follow = src.GetComponent<SfxFollow>();
            if (follow != null) follow.enabled = false;   // a voice taken over: no longer the moving object's
            src.transform.position = position;
            src.clip = GoF2Remake.Modding.ModSounds.Get(clip);
            src.volume = v;
            src.panStereo = Pan(position);   // Modified: heard from its side
            src.Play();
            return src;
        }

        /// <summary>A free voice, else the quietest one if it is quieter than 'volume'; null = none.</summary>
        static AudioSource Voice(float volume)
        {
            if (holder == null)
            {
                holder = new GameObject("Sfx voices");
                voices = new AudioSource[Voices];
                for (int i = 0; i < Voices; i++)
                {
                    var go = new GameObject("Sfx");
                    go.transform.SetParent(holder.transform, false);
                    var s = go.AddComponent<AudioSource>();
                    s.playOnAwake = false;
                    s.spatialBlend = 0f;
                    voices[i] = s;
                }
            }
            AudioSource quietest = null;
            foreach (var s in voices)
            {
                // Its pitch is left alone: TimeExtender pitches the sources it finds and puts their pitch back itself.
                // A voice that never played has no clip: its time can't be read (Unity warns on every read).
                if (!s.isPlaying && (s.clip == null || s.time <= 0f)) return s;
                if (quietest == null || s.volume < quietest.volume) quietest = s;
            }
            if (quietest == null || quietest.volume >= volume) return null;
            quietest.Stop();
            return quietest;
        }
    }
}
