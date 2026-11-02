// LotTones.cs
// Remake: the soft tones of the Mining Plant's waste sorting page, one as a ton with a find lights (PauseMenu.LotSound,
// FlightHud.PlayLotTone). Made here rather than taken from the game's banks: a short sine pip, pitched by the find's tier
// on a pentatonic scale so a run of finds sounds like a little tune (nothing usable a low muted thud, unused since junk
// tons are silent; Scrap C5, Salvage E5, Valuable G5, Rare C6 + E6, Exceptional E6 + G6), 5 ms attack and an exponential fade to silence so it never
// clicks. Measured (2026-11-02, then 6 dB louder on request): about -25 to -27 dB RMS at full effects volume, over the
// menus' hover click (-37) and under the best find's sound at the end (-11 to -18).

using UnityEngine;
using Tier = GoF2Remake.Data.WasteProcessing.FindTier;

namespace GoF2Remake.UI
{
    public static class LotTones
    {
        public const int SampleRate = 44100;

        /// <summary>A tier's notes (Hz), its length (ms) and its loudness (0..1, before the effects volume).</summary>
        public static (float[] notes, int ms, float volume) Shape(Tier tier) => tier switch
        {
            Tier.None => (new[] { 196f }, 70, 0.20f),
            Tier.Scrap => (new[] { 523.25f }, 90, 0.32f),
            Tier.Salvage => (new[] { 659.25f }, 100, 0.36f),
            Tier.Valuable => (new[] { 783.99f }, 120, 0.40f),
            // two notes share the loudness (Samples): these a little louder so the chime stands out over a Valuable pip
            Tier.Rare => (new[] { 1046.5f, 1318.5f }, 220, 0.56f),
            _ => (new[] { 1318.5f, 1568f }, 280, 0.64f),
        };

        /// <summary>The samples of a tier's tone (mono, SampleRate): its notes (a second one starting a third of the way in, a
        /// chime), each a sine with a quiet octave above; nothing usable is a duller triangle-like thud.</summary>
        public static float[] Samples(Tier tier)
        {
            var (notes, ms, _) = Shape(tier);
            int n = SampleRate * ms / 1000;
            var data = new float[n];
            float attack = SampleRate * 0.005f;
            for (int k = 0; k < notes.Length; k++)
            {
                int start = k * n / 3;
                for (int i = start; i < n; i++)
                {
                    float t = (i - start) / (float)SampleRate;
                    float life = (i - start) / (float)(n - start);
                    float env = Mathf.Min(1f, (i - start) / attack) * Mathf.Exp(-5f * life) * (1f - life);   // to 0 at the end
                    float w = 2f * Mathf.PI * notes[k] * t;
                    float s = tier == Tier.None
                        ? Mathf.Sin(w) + 0.3f * Mathf.Sin(3f * w) / 9f   // odd harmonic: dull
                        : Mathf.Sin(w) + 0.25f * Mathf.Sin(2f * w);      // octave: bright
                    data[i] += 0.8f * env * s / notes.Length;
                }
            }
            return data;
        }

        /// <summary>A tier's tone as a clip (made on first use by the caller and kept).</summary>
        public static AudioClip Clip(Tier tier)
        {
            var samples = Samples(tier);
            var clip = AudioClip.Create("WasteLot_" + tier, samples.Length, 1, SampleRate, false);
            clip.SetData(samples, 0);
            return clip;
        }
    }
}
