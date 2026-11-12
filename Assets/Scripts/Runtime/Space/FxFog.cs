// FxFog.cs
// Modified: the burning Luur station's smoke (sn_burning_station_fire_anim_alpha and the intro's
// sn_burning_station_fire_intro_anim_alpha) on GoF2/FxFog, the AlphaBlend Shader Graph's look with the distance fog the
// station's hull has (URP 17's Shader Graph transparents take no linear / exp fog): it stood out clear against the
// fogged station (a player's report). The fire (_add) keeps its own look, as the player chose. The copies are shared
// by every object with the same material.

using System.Collections.Generic;
using UnityEngine;

namespace GoF2Remake.Visuals
{
    [Unity.Scripting.LifecycleManagement.NoAutoStaticsCleanup]
    public static class FxFog
    {
        const string ShaderPath = "GoF2Emissive/FxFog";
        static Shader shader;
        static bool loaded;
        static readonly Dictionary<Material, Material> cache = new Dictionary<Material, Material>();

        static readonly int MainTexId = Shader.PropertyToID("_MainTex"), ColorId = Shader.PropertyToID("_Color"),
            GlowId = Shader.PropertyToID("_Glow"), VertexColorId = Shader.PropertyToID("_UseVertexColor"),
            SrcBlendId = Shader.PropertyToID("_SrcBlend"), DstBlendId = Shader.PropertyToID("_DstBlend"), AdditiveId = Shader.PropertyToID("_Additive");

        /// <summary>The smoke layers under 'root' take the scene's fog (AssembledObject.Awake).</summary>
        public static void Apply(GameObject root)
        {
            if (root == null) return;
            foreach (var r in root.GetComponentsInChildren<Renderer>(true))
            {
                string n = r.gameObject.name;
                if (r is ParticleSystemRenderer || !n.Contains("burning_station_fire") || !n.Contains("_alpha")) continue;
                if (!loaded) { loaded = true; shader = Resources.Load<Shader>(ShaderPath); }
                if (shader == null) return;
                var mats = r.sharedMaterials;
                bool changed = false;
                for (int i = 0; i < mats.Length; i++)
                {
                    var m = mats[i];
                    if (m == null || m.shader == null) continue;
                    bool additive = m.shader.name == "GoF2/Additive";
                    if (!additive && m.shader.name != "GoF2/AlphaBlend") continue;
                    if (!cache.TryGetValue(m, out var fogged) || fogged == null)
                    {
                        fogged = new Material(shader) { name = m.name + " (fog)", renderQueue = m.renderQueue };
                        if (m.HasProperty(MainTexId))
                        {
                            fogged.SetTexture(MainTexId, m.GetTexture(MainTexId));
                            fogged.SetTextureScale(MainTexId, m.GetTextureScale(MainTexId));
                            fogged.SetTextureOffset(MainTexId, m.GetTextureOffset(MainTexId));
                        }
                        if (m.HasProperty(ColorId)) fogged.SetColor(ColorId, m.GetColor(ColorId));
                        if (m.HasProperty(GlowId)) fogged.SetFloat(GlowId, m.GetFloat(GlowId));
                        if (m.HasProperty(VertexColorId)) fogged.SetFloat(VertexColorId, m.GetFloat(VertexColorId));
                        // The graphs' blends: Additive = SrcAlpha One, Alpha = SrcAlpha OneMinusSrcAlpha.
                        fogged.SetFloat(SrcBlendId, (float)UnityEngine.Rendering.BlendMode.SrcAlpha);
                        fogged.SetFloat(DstBlendId, (float)(additive ? UnityEngine.Rendering.BlendMode.One : UnityEngine.Rendering.BlendMode.OneMinusSrcAlpha));
                        fogged.SetFloat(AdditiveId, additive ? 1f : 0f);
                        cache[m] = fogged;
                    }
                    mats[i] = fogged;
                    changed = true;
                }
                if (changed) r.sharedMaterials = mats;
            }
        }
    }
}
