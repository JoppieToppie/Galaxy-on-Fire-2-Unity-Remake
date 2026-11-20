// FxFlash.cs
// Remake mods (2026-11-11, after Chorus's explosions' lights-only Cascade emitters, ParticleModuleLight): a death effect's
// "light" emitter (ships.json "deathFx", ModDeathFx) as one point light: from its first burst for its longest life, its
// range 'light' (game units) x the size over life, its colour (normalised) x the colour over life, its intensity
// 'lightIntensity' x the colour's brightness and the alpha over life (Unreal's light follows its particle's colour, alpha
// and size: RadiusScale x size). Gone at its end. One light per emitter, not per particle (Chorus's light emitters burst
// one). A "loop" emitter (2026-11-19, the drift's: Cascade's EmitterLoops 0) flashes again every 'duration' until
// StopLooping, then ends with its flash.

using GoF2Remake.Data;
using UnityEngine;

namespace GoF2Remake.World
{
    public class FxFlash : MonoBehaviour
    {
        const float M = 0.05f;

        public Light Light { get; private set; }
        /// <summary>Seconds from its spawn to its end.</summary>
        public float Lasts => delayS + lifeS;
        float delayS, lifeS, range, intensity, t, periodS;
        /// <summary>It flashes again every period ("loop") until StopLooping.</summary>
        public bool Looping { get; private set; }
        Color colour = Color.white;
        float[] colorOverLife, alphaOverLife, sizeOverLife;

        /// <summary>The emitter's light under 'root' ('scale' as the effect's).</summary>
        public static FxFlash Spawn(CustomFxEmitter e, Transform root, float scale)
        {
            var go = new GameObject("Flash");
            go.transform.SetParent(root, false);
            if (e.at != null && e.at.Length >= 3) go.transform.localPosition = new Vector3(e.at[0], e.at[1], e.at[2]) * M * Mathf.Max(0.01f, scale);   // ("at")
            var f = go.AddComponent<FxFlash>();
            f.Light = go.AddComponent<Light>();
            f.Light.type = LightType.Point;
            f.Light.shadows = LightShadows.None;
            f.Light.enabled = false;
            float first = float.MaxValue;
            if (e.bursts != null) for (int i = 0; i + 1 < e.bursts.Length; i += 2) if (e.bursts[i + 1] > 0f) first = Mathf.Min(first, e.bursts[i]);
            f.delayS = first < float.MaxValue ? Mathf.Max(0f, first) : 0f;
            f.lifeS = Mathf.Max(0.05f, e.life != null && e.life.Length > 0 ? e.life[e.life.Length > 1 ? 1 : 0] : 1f);
            f.Looping = e.loop;
            f.periodS = Mathf.Max(0.05f, e.duration);
            f.range = e.light * M * Mathf.Max(0.01f, scale);
            var c = e.color != null && e.color.Length >= 3 ? new Color(e.color[0], e.color[1], e.color[2]) : Color.white;
            float peak = Mathf.Max(c.r, Mathf.Max(c.g, c.b));
            f.colour = peak > 1e-6f ? c / peak : Color.white;
            f.intensity = Mathf.Max(0f, e.lightIntensity);
            f.colorOverLife = e.colorOverLife; f.alphaOverLife = e.alphaOverLife; f.sizeOverLife = e.sizeOverLife;
            f.Apply();
            return f;
        }

        /// <summary>'samples' spread evenly over 0..1 (every 'stride'-th from 'offset'), read at 'u'; 'fallback' without.</summary>
        public static float Sample(float[] samples, float u, float fallback = 1f, int stride = 1, int offset = 0)
        {
            if (samples == null) return fallback;
            int n = samples.Length / stride;
            if (n <= 0) return fallback;
            if (n == 1) return samples[offset];
            float x = Mathf.Clamp01(u) * (n - 1);
            int i = Mathf.Min((int)x, n - 2);
            return Mathf.Lerp(samples[i * stride + offset], samples[(i + 1) * stride + offset], x - i);
        }

        /// <summary>The light at 'u' (0..1) of its life: (colour, intensity, range in metres).</summary>
        public (Color colour, float intensity, float range) At(float u)
        {
            var c = colour;
            if (colorOverLife != null && colorOverLife.Length >= 3)
                c = new Color(c.r * Sample(colorOverLife, u, 1f, 3, 0), c.g * Sample(colorOverLife, u, 1f, 3, 1), c.b * Sample(colorOverLife, u, 1f, 3, 2));
            float bright = Mathf.Max(c.r, Mathf.Max(c.g, c.b));
            var hue = bright > 1e-6f ? c / bright : colour;
            float top = 1e-6f;
            if (sizeOverLife != null) foreach (var v in sizeOverLife) top = Mathf.Max(top, v);
            float size = sizeOverLife != null && sizeOverLife.Length > 0 ? Sample(sizeOverLife, u) / top : 1f;
            return (hue, intensity * bright * Mathf.Clamp01(Sample(alphaOverLife, u)), range * size);
        }

        void Apply()
        {
            if (Light == null) return;
            bool on = t >= delayS && t < delayS + lifeS;
            Light.enabled = on;
            if (!on) return;
            var (c, i, r) = At((t - delayS) / lifeS);
            Light.color = c; Light.intensity = i; Light.range = Mathf.Max(0.01f, r);
        }

        /// <summary>A "loop" light: no flash after this one; it ends with it.</summary>
        public void StopLooping() => Looping = false;

        /// <summary>'dt' seconds on (Update; the tests step it).</summary>
        public void Step(float dt)
        {
            t += dt;
            if (Looping) while (t >= delayS + periodS) t -= periodS;   // (the next flash; its life past the period is cut short)
            Apply();
            if (!Looping && t >= delayS + lifeS && Application.isPlaying) Destroy(gameObject);
        }

        void Update() => Step(Time.deltaTime);
    }
}
