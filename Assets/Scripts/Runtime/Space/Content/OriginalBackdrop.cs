// OriginalBackdrop.cs
// Feature: docs/notes/player-assets.md "The content layer": the far-plane materials of a build without the originals
// (Resources/GoF2Backdrop, SpaceSceneBuilder.BuildBackdropMaterials / BuildSkyLayers), made from the player's converted
// copy when Resources has none, with the builder's settings:
//   suns (Textures/main|supernova/suns, supernova/fx/sn_supernova)  GoF2/Backdrop, additive (One One), queue 2900
//   planets and rings (Textures/main|valkyrie|supernova/planets)    GoF2/Backdrop, alpha, queue 2901 (sn_planet_ring 2902)
//   space_particle, fog, v_fog_ice                                  GoF2/SpaceDust, SrcAlpha One, queue 3000
//   sky_* (SkyLayerAssets' layers)                                  GoF2/SkyLayer with their blend, wrap and lighting
// The textures clamped (a single planet or sun image: no bleeding from the opposite edge), the flares repeated.

using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using GoF2Remake.Data;
using UnityEngine;
using UnityEngine.Rendering;

namespace GoF2Remake.Visuals
{
    [Unity.Scripting.LifecycleManagement.NoAutoStaticsCleanup]
    public static class OriginalBackdrop
    {
        public const string Folder = "GoF2Backdrop";

        static readonly Dictionary<string, Material> made = new Dictionary<string, Material>(StringComparer.OrdinalIgnoreCase);

        [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.SubsystemRegistration)]
        static void ResetStatics()
        {
            made.Clear();
            GameContent.Refreshed -= ResetStatics;
            GameContent.Refreshed += ResetStatics;   // (a new copy converted)
        }

        /// <summary>A backdrop material by its GoF2Backdrop name ("sun_004", "planet_010_big", "sky_storms"): Resources'
        /// own, else made from the converted copy; null when neither has it.</summary>
        public static Material Get(string name)
        {
            if (string.IsNullOrEmpty(name)) return null;
            var m = Resources.Load<Material>($"{Folder}/{name}");
            if (m != null) return m;
            if (made.TryGetValue(name, out m)) return m;
            m = GameContent.Installed ? Make(name) : null;
            if (m != null || GameContent.Installed) made[name] = m;
            return m;
        }

        // the sky layers' textures and settings (SpaceSceneBuilder.BuildSkyLayers)
        static readonly Dictionary<string, (string tex, bool additive, bool repeat, bool lit)> SkyLayers =
            new Dictionary<string, (string, bool, bool, bool)>(StringComparer.OrdinalIgnoreCase)
            {
                ["sky_planet_ring"] = ("supernova/skyboxes/sn_skybox_planet_ring", false, false, false),
                ["sky_storms"] = ("supernova/skyboxes/sn_skybox_storms", true, false, false),
                ["sky_supernova_flares"] = ("supernova/skyboxes/sn_skybox_015_flares", true, true, false),
                ["sky_supernova_flares_nasty"] = ("supernova/skyboxes/sn_skybox_015_flares_nasty", true, true, false),
                ["sky_asteroid_belt"] = ("main/skyboxes/skybox_asteroid_belt_diffuse", false, false, true),
            };

        static readonly Dictionary<string, string> Dust = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase)
        {
            ["space_particle"] = "main/misc/space_particle_diffuse",
            ["fog"] = "main/fx/fog",
            ["v_fog_ice"] = "valkyrie/fx/v_fog_ice",
        };

        static Material Make(string name)
        {
            if (SkyLayers.TryGetValue(name, out var sky))
            {
                var m = New("GoF2/SkyLayer", name, sky.tex, sky.repeat, sky.additive ? BlendMode.One : BlendMode.SrcAlpha,
                            sky.additive ? BlendMode.One : BlendMode.OneMinusSrcAlpha, -1);
                if (m == null) return null;
                m.SetFloat("_Lit", sky.lit ? 1f : 0f);
                if (name.Equals("sky_asteroid_belt", StringComparison.OrdinalIgnoreCase))
                {
                    // Remake: the belt's normal map and specular (material 27160, bump shader 28)
                    m.SetTexture("_BumpMap", OriginalTextures.Get("Textures/main/skyboxes/skybox_asteroid_belt_normal_specular.png"));
                    m.SetTexture("_SpecMap", OriginalTextures.Get("Textures/main/skyboxes/skybox_asteroid_belt_metallic_smoothness.png"));
                }
                return m;
            }
            if (Dust.TryGetValue(name, out var dust)) return New("GoF2/SpaceDust", name, dust, false, BlendMode.SrcAlpha, BlendMode.One, 3000);
            if (name.Equals("sn_supernova", StringComparison.OrdinalIgnoreCase))
                return New("GoF2/Backdrop", name, "supernova/fx/sn_supernova", false, BlendMode.One, BlendMode.One, 2900);
            foreach (var pack in new[] { "main", "supernova" })
                if (Exists($"{pack}/suns/{name}")) return New("GoF2/Backdrop", name, $"{pack}/suns/{name}", false, BlendMode.One, BlendMode.One, 2900);
            foreach (var pack in new[] { "main", "valkyrie", "supernova" })
                if (Exists($"{pack}/planets/{name}"))
                    return New("GoF2/Backdrop", name, $"{pack}/planets/{name}", false, BlendMode.SrcAlpha, BlendMode.OneMinusSrcAlpha,
                               name.Equals("sn_planet_ring", StringComparison.OrdinalIgnoreCase) ? 2902 : 2901);
            return null;
        }

        static bool Exists(string tex) => GameContent.CachePath($"Textures/{tex}.png") != null;

        static Material New(string shaderName, string name, string tex, bool repeat, BlendMode src, BlendMode dst, int queue)
        {
            var shader = Shader.Find(shaderName);
            var t = OriginalTextures.Get($"Textures/{tex}.png");
            if (shader == null || t == null) return null;
            t.wrapMode = repeat ? TextureWrapMode.Repeat : TextureWrapMode.Clamp;
            var m = new Material(shader) { name = name };
            m.SetTexture("_MainTex", t);
            m.SetFloat("_SrcBlend", (float)src);
            m.SetFloat("_DstBlend", (float)dst);
            if (queue > 0) m.renderQueue = queue;
            return m;
        }

        /// <summary>Every backdrop texture's name (the Mod Builder's list of planets, suns and rings to replace): Resources'
        /// materials', else the converted copy's suns and planets.</summary>
        public static List<string> TextureNames()
        {
            var names = Resources.LoadAll<Material>(Folder).Select(m => m.mainTexture != null ? m.mainTexture.name : null).Where(x => x != null).ToList();
            if (names.Count == 0 && GameContent.Installed)
                foreach (var pack in new[] { "main", "valkyrie", "supernova" })
                    foreach (var kind in new[] { "suns", "planets" })
                    {
                        string dir = Path.Combine(GameContent.Root, "Textures", pack, kind);
                        if (Directory.Exists(dir))
                            names.AddRange(Directory.GetFiles(dir, "*.png").Select(Path.GetFileNameWithoutExtension)
                                                    .Where(n => !n.EndsWith("_metallic_smoothness") && !n.EndsWith("_normal_specular")));
                    }
            return names.Distinct(StringComparer.OrdinalIgnoreCase).OrderBy(x => x, StringComparer.OrdinalIgnoreCase).ToList();
        }
    }
}
