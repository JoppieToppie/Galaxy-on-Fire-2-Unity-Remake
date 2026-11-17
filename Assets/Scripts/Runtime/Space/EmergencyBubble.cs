// EmergencyBubble.cs
// The emergency system's v_shield bubble (combat_equipment.md 2, PlayerEgo::tryToStartEmergencySystem 0xad6f0): the
// CombatAssets.shieldBubble model around a ship's model, drawn with EnergyShield (GoF2/ShieldBubble: CloakPass's copy of the
// frame bent by its normal map, most at its rim, plus a blue rim), sized by the model's bounds (radius / 500 + 0.1), growing over the first
// 5 % of its time and shrinking over the last 5 %. PlayerHealth shows it on the player's ship; multiplayer: the other
// players' copies of that ship show it too (NetPlayer, from the owner's emergency clock).

using System.Collections.Generic;
using UnityEngine;

namespace GoF2Remake.Flight
{
    public sealed class EmergencyBubble
    {
        const float M = 0.05f;
        GameObject bubble;
        readonly List<Material> materials = new List<Material>();
        float scale;

        /// <summary>The bubble on 'parent' (the ship's model), sized by 'sizeFrom' (null: 1500 units); null without the asset.</summary>
        public static EmergencyBubble Show(CombatAssets assets, Transform parent, Renderer sizeFrom)
        {
            if (assets == null || assets.shieldBubble == null || parent == null) return null;
            var b = new EmergencyBubble { bubble = UnityEngine.Object.Instantiate(assets.shieldBubble, parent, false) };
            GunRig.StripForFx(b.bubble);
            if (assets.shieldBubbleShader != null)
                foreach (var br in b.bubble.GetComponentsInChildren<Renderer>())
                {
                    var src = br.sharedMaterial;
                    var mat = new Material(assets.shieldBubbleShader);
                    if (src != null && src.HasProperty("_BumpMap")) mat.SetTexture("_NormalMap", src.GetTexture("_BumpMap"));   // v_shield_normal (s_texture[1])
                    // Modified: v_shield_noise (s_texture[0]) for the PC version's energy bands
                    if (src != null && src.HasProperty("_BaseMap")) mat.SetTexture("_NoiseMap", src.GetTexture("_BaseMap"));
                    else if (src != null && src.HasProperty("_MainTex")) mat.SetTexture("_NoiseMap", src.GetTexture("_MainTex"));
                    br.sharedMaterial = mat;
                    b.materials.Add(mat);
                }
            GoF2Remake.Visuals.CloakPass.Request(b, true);   // the bubble refracts CloakPass's copy of the frame
            float radiusUnits = sizeFrom != null ? sizeFrom.bounds.extents.magnitude / M : 1500f;
            b.scale = radiusUnits / 500f + 0.1f;
            b.bubble.transform.localScale = Vector3.zero;
            return b;
        }

        /// <summary>'leftMs' of 'lengthMs' to go: the size (5 % in, 5 % out) and the shader's clock.</summary>
        public void Update(float leftMs, float lengthMs)
        {
            if (bubble != null)
            {
                float t = leftMs, d = Mathf.Max(1f, lengthMs), edge = 0.05f * d;
                float f = t > d - edge ? (d - t) / edge : t < edge ? t / edge : 1f;
                bubble.transform.localScale = Vector3.one * scale * Mathf.Clamp01(f);
            }
            foreach (var m in materials) if (m != null) m.SetFloat("_Anim", (lengthMs - leftMs) * 0.001f);   // mesh+0x24 += dt * 0.001 (the phone's shader never uses it; the PC version's bands run on it)
        }

        /// <summary>Gone: the model, its materials and the CloakPass request.</summary>
        public void Hide()
        {
            if (bubble != null) UnityEngine.Object.Destroy(bubble);
            bubble = null;
            foreach (var m in materials) if (m != null) UnityEngine.Object.Destroy(m);
            materials.Clear();
            GoF2Remake.Visuals.CloakPass.Request(this, false);
        }
    }
}
