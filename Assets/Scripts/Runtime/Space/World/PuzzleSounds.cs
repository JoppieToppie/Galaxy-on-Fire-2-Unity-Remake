// PuzzleSounds.cs
// Remake (2026-11-06, for mods): the secure containers' puzzle sounds (stations.json "orbit.containers.sounds": the mod's
// sounds by role, Modding.ModWorld.PuzzleSoundRoles), after EVERSPACE 2's cues: grab S_SC_Grab_Hold, hold S_SC_Grab_Hold_Loop
// (looped on what the ship carries), drop S_SC_Grab_Leggo, throw S_SC_Grab_Throw (BP_Portable_Component), dispenser
// S_SC_OrbDispenser_Loop (looped at a dispenser), conductor S_SC_3D_ConductorBeam (looped at an energized receiver), unlock
// S_SC_Container_Armored (a secure container opening). Set per orbit when its containers are laid out (Use); 3D at the
// object, GoF2's effects volume. ES2's shot triggers play nothing of their own (no TriggerSound placed), and neither do these.

using System.Collections.Generic;
using GoF2Remake.Data;
using UnityEngine;

namespace GoF2Remake.World
{
    [Unity.Scripting.LifecycleManagement.NoAutoStaticsCleanup]
    public static class PuzzleSounds
    {
        /// <summary>The 3D falloff (m): full within MinM, gone by MaxM.</summary>
        public const float MinM = 60f, MaxM = 1500f;
        static Dictionary<string, string> names;
        [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.SubsystemRegistration)]
        static void ResetStatics() => names = null;

        /// <summary>The orbit's sounds (null = silent).</summary>
        public static void Use(Dictionary<string, string> roles) => names = roles;

        public static AudioClip Clip(string role) => names != null && names.TryGetValue(role, out var n) ? Modding.ModSounds.Named(n) : null;

        /// <summary>A one-shot of 'role' at 'at'.</summary>
        public static void PlayAt(string role, Vector3 at)
        {
            var clip = Clip(role);
            if (clip == null) return;
            var go = new GameObject("Puzzle sound " + role);
            go.transform.position = at;
            var src = Source(go, clip, false);
            src.Play();
            Object.Destroy(go, clip.length + 0.1f);
        }

        /// <summary>A loop of 'role' on 'owner' (null when the orbit has none); stop it by destroying it or pausing it.</summary>
        public static AudioSource Loop(string role, GameObject owner)
        {
            var clip = Clip(role);
            if (clip == null || owner == null) return null;
            var src = Source(owner, clip, true);
            src.Play();
            return src;
        }

        static AudioSource Source(GameObject go, AudioClip clip, bool loop)
        {
            var src = go.AddComponent<AudioSource>();
            src.clip = clip;
            src.loop = loop;
            src.playOnAwake = false;
            src.spatialBlend = 1f;
            src.rolloffMode = AudioRolloffMode.Linear;
            src.minDistance = MinM;
            src.maxDistance = MaxM;
            src.dopplerLevel = 0f;
            src.volume = Settings.SfxVolume;
            return src;
        }
    }
}
