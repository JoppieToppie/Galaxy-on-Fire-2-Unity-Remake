// OriginalContent.cs
// Feature: docs/notes/player-assets.md "The content layer": the original's materials and prefabs that the remake's own
// assets point at (OriginalRefs' manifest: CombatAssets' sprites and wrecks, StarMapAssets' suns, SkyLayerAssets, the
// story's intro skies, the station's bar visitor glows, ModAssets), made from the player's converted copy by their
// project path. GameHooks.ResolveOriginal (SpaceHooksInstaller) lets OriginalRefs (GoF2.Game) reach them:
//   Materials/mat_<id>_<name>.mat          OriginalMaterials.For(id) (PrefabBuilder's material for resources.json's id)
//   Materials/sn_galaxymap_sun_012.mat     StarMapBuilder's copy of material 27307 showing sn_sun_012 whole
//   Resources/GoF2Backdrop/<name>.mat      OriginalBackdrop.Get
//   Skyboxes/skybox_<n>.mat                OriginalSkies.Skybox (the old combined bakes)
//   Prefabs/<rel>.prefab                   OriginalAssemblies.ModelTemplate("Models/<rel>.fbx") (a per-mesh prefab)
//   Resources/Assembled/.../<name>.prefab  OriginalAssemblies.Template (an assembled object)

using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Text.RegularExpressions;
using GoF2Remake.Data;
using UnityEngine;

namespace GoF2Remake.Visuals
{
    [Unity.Scripting.LifecycleManagement.NoAutoStaticsCleanup]
    public static class OriginalContent
    {
        static readonly Regex MaterialId = new Regex(@"^Materials/mat_(\d+)_", RegexOptions.IgnoreCase);
        static readonly Regex SkyboxIndex = new Regex(@"^Skyboxes/skybox_(\d+)\.mat$", RegexOptions.IgnoreCase);
        static Material galaxyMapSun;

        [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.SubsystemRegistration)]
        static void ResetStatics()
        {
            galaxyMapSun = null;
            GameContent.Refreshed -= ResetStatics;
            GameContent.Refreshed += ResetStatics;   // (a new copy converted)
        }

        /// <summary>The original's asset at 'projectPath' as 'type' wants it (Material, GameObject), null when it isn't one of
        /// these kinds or the copy hasn't it.</summary>
        public static object Resolve(Type type, string projectPath)
        {
            if (string.IsNullOrEmpty(projectPath) || !GameContent.Installed) return null;
            string p = projectPath.Replace('\\', '/');
            if (typeof(Material).IsAssignableFrom(type))
            {
                var m = MaterialId.Match(p);
                if (m.Success) return OriginalMaterials.For(int.Parse(m.Groups[1].Value));
                if (p.Equals("Materials/sn_galaxymap_sun_012.mat", StringComparison.OrdinalIgnoreCase)) return GalaxyMapSun();
                if (p.StartsWith("Resources/GoF2Backdrop/", StringComparison.OrdinalIgnoreCase))
                    return OriginalBackdrop.Get(Path.GetFileNameWithoutExtension(p));
                var s = SkyboxIndex.Match(p);
                if (s.Success) return OriginalSkies.Skybox(int.Parse(s.Groups[1].Value));
                return null;
            }
            if (typeof(GameObject).IsAssignableFrom(type) || typeof(Component).IsAssignableFrom(type))
            {
                GameObject go = null;
                if (p.StartsWith("Prefabs/", StringComparison.OrdinalIgnoreCase) && p.EndsWith(".prefab", StringComparison.OrdinalIgnoreCase))
                    go = OriginalAssemblies.ModelTemplate("Models/" + p.Substring(8, p.Length - 8 - 7) + ".fbx");
                else if (p.StartsWith("Resources/Assembled/", StringComparison.OrdinalIgnoreCase))
                    go = OriginalAssemblies.Template(new AssemblyData { name = Path.GetFileNameWithoutExtension(p) });
                if (go == null || typeof(GameObject).IsAssignableFrom(type)) return go;
                return go.GetComponent(type);
            }
            return null;
        }

        /// <summary>The folder (Resources) of the template materials that keep the runtime materials' shader variants in a
        /// build without the originals: one per shader and keyword set EveryMaterial makes, textures left out
        /// ("GoF2 > Build > Runtime Shader Templates", RuntimeShaderTemplates). Without them a player build strips what no
        /// material of its own uses (URP/Lit with a normal map, transparent...), and the game's materials draw wrong.</summary>
        public const string TemplateFolder = "GoF2Content/ShaderTemplates";

        /// <summary>A material's variant: its shader and its keywords.</summary>
        public static string VariantKey(Material m) =>
            m == null || m.shader == null ? null : m.shader.name + "|" + string.Join(" ", m.shaderKeywords.OrderBy(k => k, StringComparer.Ordinal));

        /// <summary>Every material the content layer makes from the converted copy (the resource table's, the backdrop's,
        /// the galaxy map's sun) and the plain ones the skies render with: what the shader templates cover.</summary>
        public static IEnumerable<Material> EveryMaterial()
        {
            var table = OriginalMaterials.Table;
            if (table.materials != null)
                foreach (var m in table.materials) { var made = OriginalMaterials.For(m.id); if (made != null) yield return made; }
            foreach (var n in OriginalBackdrop.TextureNames().Concat(new[] { "sn_supernova", "space_particle", "fog", "v_fog_ice",
                         "sky_planet_ring", "sky_storms", "sky_supernova_flares", "sky_supernova_flares_nasty", "sky_asteroid_belt" }))
            {
                var b = OriginalBackdrop.Get(n);
                if (b != null) yield return b;
            }
            var sun = GalaxyMapSun();
            if (sun != null) yield return sun;
            // OriginalSkies: the sky layers' plain GoF2/Unlit and GoF2/Additive, the intros' Skybox/Cubemap
            foreach (var n in new[] { "GoF2/Unlit", "GoF2/Additive", "Skybox/Cubemap" })
            {
                var sh = Shader.Find(n);
                if (sh != null) yield return new Material(sh) { name = n };
            }
        }

        /// <summary>StarMapBuilder: the map's supernova sun, material 27307 showing sn_sun_012 whole (not the atlas corner).</summary>
        static Material GalaxyMapSun()
        {
            if (galaxyMapSun != null) return galaxyMapSun;
            var from = OriginalMaterials.For(27307);
            var tex = OriginalTextures.Get("Textures/supernova/suns/sn_sun_012.png");
            if (from == null || tex == null) return from;
            galaxyMapSun = new Material(from) { name = "sn_galaxymap_sun_012" };
            galaxyMapSun.SetTexture("_MainTex", tex);
            galaxyMapSun.SetTextureScale("_MainTex", Vector2.one);
            galaxyMapSun.SetTextureOffset("_MainTex", Vector2.zero);
            return galaxyMapSun;
        }
    }
}
