// GoF2SkyLayers.cs
// Remake (2026-11-14, the user: "combine es2s methods with chorus methods for the skies", "es2 shouldn't be required for this.
// recreate it", "4 options"): the game's own 19 nebula skies as layered skies (GoF2/SpaceSky's layered sky, SpaceSkyCore
// ChorusSky), for Options > Graphics "Skies" (Settings.SkyStyle). Each sky's layout (Resources/GoF2SkyLayers/layered_skies.json,
// Reference/tools/sky/sky_layers.py, from the original bakes): its bright regions (direction in the baked cube's frame, size,
// colour, brightness), the plane its nebulae lie in, its main colour and coverage, its patch. The images are the remake's own
// ("gof2:" names: star_mask, nebula_masks, patch_0XX), nothing of EVERSPACE 2's or Chorus's.
//   Enhanced: Chorus's way: the brightest part again as a sharper image patch over the (upscaled) painted sky.
//   EVERSPACE 2 style: as ES2 builds a location (its sky levels and BP_EnvironmentGenerator, read from its files): the sky
//     sphere only its clustered three-layer star field and, under the fuller skies, a faint sky-wide nebula; the nebulae are
//     3D local nebulae (World.NebulaClouds): each bright region a distant one in its direction, as wide on the sky, local
//     ones round the orbit.
//   Combined: Enhanced (the painted sky and its patch), ES2's faint star field behind GoF2's own stars, ES2's local nebulae
//     round the orbit.
using System.Collections.Generic;
using GoF2Remake.Data;
using GoF2Remake.Modding;
using UnityEngine;

namespace GoF2Remake.World
{
    [Unity.Scripting.LifecycleManagement.NoAutoStaticsCleanup]
    public static class GoF2SkyLayers
    {
        /// <summary>A bright region: its direction (the baked cube's frame), size (degrees), average colour, brightness, and its two
        /// main hues (heavier first).</summary>
        [System.Serializable] public sealed class Region { public float[] dir; public float radius; public float[] color; public float brightness; public float[] colorA, colorB; }
        [System.Serializable] public sealed class Patch { public string image; public float[] dir; public float span; public float heading, elevation; }
        [System.Serializable] public sealed class Sky
        {
            public float coverage, brightness, planarity;
            public float[] main, bandNormal;
            public Region[] regions;
            public Patch patch;
        }
        [System.Serializable] sealed class Table { public Sky[] skies; }

        static Table table;
        [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.SubsystemRegistration)] static void ResetStatics() => table = null;

        /// <summary>GoF2 nebula sky 'index''s layout (0-18); null without one.</summary>
        public static Sky Of(int index)
        {
            if (table == null)
            {
                var asset = Resources.Load<TextAsset>("GoF2SkyLayers/layered_skies");
                table = asset != null ? JsonUtility.FromJson<Table>(asset.text) : new Table { skies = new Sky[0] };
            }
            return table.skies != null && index >= 0 && index < table.skies.Length ? table.skies[index] : null;
        }

        /// <summary>A layered-sky layer's heading / elevation (turns, Chorus's units) whose patch is centred on 'dir' (the baked
        /// cube's frame): SpaceSkyCore ChorusLayer's patch middle q = (0, -1, 0) is the view (cos e sin h, cos e cos h, -sin e).</summary>
        public static void Toward(Vector3 dir, out float heading, out float elevation)
        {
            dir.Normalize();
            float e = -Mathf.Asin(Mathf.Clamp(dir.z, -1f, 1f));
            float h = Mathf.Atan2(dir.x, dir.y);
            heading = h / (2f * Mathf.PI);
            elevation = -e / (0.5f * Mathf.PI);
        }

        static Vector3 V(float[] a) => a != null && a.Length >= 3 ? new Vector3(a[0], a[1], a[2]) : Vector3.forward;
        static Color C(float[] a, float k) => a != null && a.Length >= 3 ? new Color(a[0] * k, a[1] * k, a[2] * k, 1f) : new Color(k, k, k, 1f);

        /// <summary>'c' more saturated by 'amount' (1: as it is): GoF2's region colours are pale averages, EVERSPACE 2's nebulae
        /// vivid.</summary>
        static Color Vivid(Color c, float amount)
        {
            float l = 0.2126f * c.r + 0.7152f * c.g + 0.0722f * c.b;
            return new Color(Mathf.Max(0f, l + (c.r - l) * amount), Mathf.Max(0f, l + (c.g - l) * amount), Mathf.Max(0f, l + (c.b - l) * amount), 1f);
        }

        /// <summary>Sky 'index''s layers for 'style' (Settings.SkyEnhanced / SkyEs2 / SkyCombined); null for Original or without
        /// its layout.</summary>
        public static ModWorld.ChorusSky Build(int index, int style)
        {
            var sky = Of(index);
            if (sky == null || style == Settings.SkyOriginal) return null;
            bool chorus = style == Settings.SkyEnhanced || style == Settings.SkyCombined;
            bool es2 = style == Settings.SkyEs2 || style == Settings.SkyCombined;
            bool alone = style == Settings.SkyEs2;
            var c = new ModWorld.ChorusSky { emissive = 1f, keepBase = !alone };
            c.curve0 = new Vector4(0f, 0f, 0f, 0f);
            c.curve1 = new Vector4(1f, 1f, 1f, 1f);
            var layers = new List<ModWorld.ChorusLayer>();
            // Chorus's way first (its patch covers what is under it: the ES2 layers go over it)
            if (chorus && sky.patch != null && !string.IsNullOrEmpty(sky.patch.image))
                layers.Add(new ModWorld.ChorusLayer
                {
                    kind = ModWorld.ChorusLayer.Image, image = "gof2:" + sky.patch.image, heading = sky.patch.heading, elevation = sky.patch.elevation,
                    scale = sky.patch.span / 180f, emissive = 0f, exponent = 1f, opacity = 1f,
                });

            if (es2)
            {
                // ES2's star field (M_Skybox_Stars: its three layers r + g / 2 + b / 4, tiled 2 x 1 round its sky sphere, thinned by
                // a noise at its Param 2, ^2.5); behind GoF2's own stars, only its faint ones
                c.stars = "gof2:star_mask";
                c.starsTiling = 2f; c.starsTilingV = 1f;
                c.starsChannels = alone ? new Vector3(1f, 0.5f, 0.25f) : new Vector3(0.1f, 0.3f, 0.45f);
                c.starsBrightness = alone ? 1.3f : 0.5f;
                c.starsNoiseScale = 2f; c.starsNoiseExponent = 2.5f;
                // ES2's sky sphere has nothing else but, in some sectors, a sky-wide nebula (BP_Skybox_Nebula_Global, Intensity
                // 0.2): under the fuller GoF2 skies, its main colour. The nebulae themselves are 3D clouds (World.NebulaClouds:
                // the bright regions far off, local ones near), as ES2 places a sector's nebulae round a location.
                if (alone && sky.coverage > 0.3f)
                    layers.Add(new ModWorld.ChorusLayer
                    {
                        kind = ModWorld.ChorusLayer.Mask, image = "gof2:nebula_masks", channel = 2, scale = 2f, tiling = new Vector2(2f, 2f),
                        color = Vivid(C(sky.main, sky.brightness * 0.35f), 1.4f), exponent = 1.6f, sunScatter = 1f,
                    });
            }
            foreach (var l in layers) if (c.layers.Count < ModWorld.ChorusSky.MaxLayers) c.layers.Add(l);
            return c;
        }
    }
}
