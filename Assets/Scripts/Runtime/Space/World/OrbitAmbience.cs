// OrbitAmbience.cs
// Remake (2026-11-05, for mods): the orbit's ambience zones (stations.json "orbit.ambience", Modding.ModWorld.AmbienceSpec),
// after EVERSPACE 2's BP_MusicTrigger OverrideAmbience zones round its stations, factories, cargo wrecks and wreck fields:
// each zone loops one of its sounds (picked at random, as ES2's SoundNodeRandom) and plays one of its "adds" now and then
// (ES2's SoundNodeDelay over a SoundNodeRandom, mixed in at its own volume); full volume within its radius of its centre,
// fading out over half the radius more (ES2's inner and outer spheres), everywhere without a centre. Under the music, at
// the sound effects' volume; the sounds are the mods' own, a loop a music track (ModMusic.Track: kept compressed, for the
// long beds) or a sound (ModSounds.Named: decoded), the adds sounds, looked up again until they have loaded.

using System.Collections.Generic;
using GoF2Remake.Data;
using UnityEngine;

namespace GoF2Remake.World
{
    public class OrbitAmbience : MonoBehaviour
    {
        const float M = 0.05f;
        const float Fade = 0.5f;   // x the radius: the fade beyond it

        sealed class Zone
        {
            public Modding.ModWorld.AmbienceSpec spec;
            public AudioSource source, addSource;
            public string loop;
            public float nextAddMs;
        }

        readonly List<Zone> zones = new List<Zone>();
        Transform listener;

        public static OrbitAmbience Attach(SpaceLevel level)
        {
            var spec = level != null && level.Layout != null && !level.Layout.interstellar ? Modding.ModWorld.OrbitOf(level.Layout.stationIndex) : null;
            if (spec?.ambience == null || level.Player == null) return null;
            var go = new GameObject("Orbit ambience");
            go.transform.SetParent(level.transform, false);
            var a = go.AddComponent<OrbitAmbience>();
            a.listener = level.Player.transform;
            foreach (var z in spec.ambience)
            {
                var src = go.AddComponent<AudioSource>();
                src.loop = true;
                src.playOnAwake = false;
                src.spatialBlend = 0f;
                src.volume = 0f;
                var adds = go.AddComponent<AudioSource>();
                adds.playOnAwake = false;
                adds.spatialBlend = 0f;
                a.zones.Add(new Zone { spec = z, source = src, addSource = adds, loop = z.loops[Random.Range(0, z.loops.Count)], nextAddMs = Random.Range(z.addMinMs, z.addMaxMs) });
            }
            return a;
        }

        /// <summary>A zone's share of its volume where the player is: 1 within its radius, 0 past half of it more.</summary>
        public static float Weight(Modding.ModWorld.AmbienceSpec z, Vector3 playerGame)
        {
            if (!z.centre.HasValue) return 1f;
            float d = Vector3.Distance(playerGame, z.centre.Value);
            return Mathf.Clamp01(1f - (d - z.radius) / (z.radius * Fade));
        }

        void Update()
        {
            if (listener == null) return;
            var p = listener.position;
            var game = new Vector3(p.x, p.y, -p.z) / M;
            float sfx = Settings.SfxVolume;
            float dtMs = Time.deltaTime * 1000f;
            foreach (var z in zones)
            {
                if (z.source.clip == null)
                {
                    var clip = Modding.ModMusic.Track(z.loop) ?? Modding.ModSounds.Named(z.loop);
                    if (clip == null) continue;   // (still loading)
                    z.source.clip = clip;
                    z.source.time = Random.Range(0f, clip.length);   // (not every orbit from the loop's start)
                    z.source.Play();
                }
                float w = Weight(z.spec, game);
                z.source.volume = z.spec.volume * w * sfx;
                if (z.spec.adds == null || z.spec.adds.Count == 0 || (z.nextAddMs -= dtMs) > 0f) continue;
                z.nextAddMs = Random.Range(z.spec.addMinMs, z.spec.addMaxMs);
                if (w <= 0.01f) continue;
                var add = Modding.ModSounds.Named(z.spec.adds[Random.Range(0, z.spec.adds.Count)]);
                if (add == null) continue;
                z.addSource.volume = 1f;
                z.addSource.PlayOneShot(add, z.spec.addVolume * w * sfx);
            }
        }
    }
}
