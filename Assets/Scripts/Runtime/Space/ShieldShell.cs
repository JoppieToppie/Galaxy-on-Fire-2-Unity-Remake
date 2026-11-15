// ShieldShell.cs
// Remake mods (2026-11-11, Chorus's BP_Effect_Auxiliary_Shield_PlayerCapitalShip: P_Buff_Auxiliary_Shield_PlayerCapitalShip,
// one mesh particle of SM_PlayerControlledLargeShip_01_Shield in MI_Shield_PlayerCapital_Auxiliary): the Shield Overcharge's
// look on a ship with ships.json "overcharge" (CustomOvercharge): its shell model under the ship's hull node
// (Modding.ModCrates.InstantiateOvercharge, the ship's materials: a "shield" block, GoF2/ChorusShield), shown "delay"
// seconds after the overcharge starts (the emitter's EmitterDelay 0.5) for "seconds" (its Lifetime 4.8), each renderer's
// _ShieldLife the particle's relative time and _ShieldTint its colour ("color" x "colorOverLife" at that time) with
// "alpha" x "alphaOverLife" (a property block). Gone at its end or with the overcharge. PlayerDevices shows it in place of the emergency
// system's bubble.

using System.Collections.Generic;
using GoF2Remake.Data;
using UnityEngine;

namespace GoF2Remake.Flight
{
    public sealed class ShieldShell
    {
        static readonly int LifeId = Shader.PropertyToID("_ShieldLife"), TintId = Shader.PropertyToID("_ShieldTint");
        GameObject shell;
        readonly List<Renderer> renderers = new List<Renderer>();
        MaterialPropertyBlock block;
        CustomOvercharge spec;
        float ageS;

        /// <summary>Ship 'ship''s shell under 'hull' (its model's hull node); null when it has none or it isn't loaded.</summary>
        public static ShieldShell Show(int ship, Transform hull)
        {
            var spec = CustomShips.Get(ship)?.overcharge;
            if (spec == null || string.IsNullOrEmpty(spec.model) || hull == null) return null;
            var go = Modding.ModCrates.InstantiateOvercharge(ship, hull);
            return go != null ? Wrap(go, spec) : null;
        }

        /// <summary>'go' (the shell model, placed) driven by 'spec' (tests).</summary>
        public static ShieldShell Wrap(GameObject go, CustomOvercharge spec)
        {
            var s = new ShieldShell { shell = go, spec = spec };
            go.name = "Shield shell";
            s.renderers.AddRange(go.GetComponentsInChildren<Renderer>(true));
            foreach (var r in s.renderers) r.shadowCastingMode = UnityEngine.Rendering.ShadowCastingMode.Off;
            s.Step(0f);
            return s;
        }

        /// <summary>Whether it is still to come or showing.</summary>
        public bool Alive => shell != null && ageS < Mathf.Max(0f, spec.delay) + Mathf.Max(0.05f, spec.seconds);

        /// <summary>The particle's relative time at 'ageS' seconds after the overcharge started (negative before its delay).</summary>
        public static float LifeAt(CustomOvercharge spec, float ageS) => (ageS - Mathf.Max(0f, spec.delay)) / Mathf.Max(0.05f, spec.seconds);

        /// <summary>Its tint at relative time 'life': the colour x the colour over life there, the alpha x the alpha over life in a.</summary>
        public static Vector4 TintAt(CustomOvercharge spec, float life)
        {
            var c = spec.color != null && spec.color.Length >= 3 ? new Vector3(spec.color[0], spec.color[1], spec.color[2]) : Vector3.one;
            float k = GoF2Remake.World.FxFlash.Sample(spec.colorOverLife, life);
            return new Vector4(c.x * k, c.y * k, c.z * k, spec.alpha * GoF2Remake.World.FxFlash.Sample(spec.alphaOverLife, life));
        }

        /// <summary>'dt' seconds on: shown from its delay, its life and tint set; gone at its end.</summary>
        public void Step(float dt)
        {
            if (shell == null) return;
            ageS += dt;
            float life = LifeAt(spec, ageS);
            if (life >= 1f) { Hide(); return; }
            bool on = life >= 0f;
            if (shell.activeSelf != on) shell.SetActive(on);
            if (!on) return;
            block ??= new MaterialPropertyBlock();
            var tint = TintAt(spec, life);
            foreach (var r in renderers)
            {
                if (r == null) continue;
                r.GetPropertyBlock(block);
                block.SetFloat(LifeId, life);
                block.SetVector(TintId, tint);
                r.SetPropertyBlock(block);
            }
        }

        /// <summary>Gone.</summary>
        public void Hide()
        {
            if (shell != null) { if (Application.isPlaying) Object.Destroy(shell); else Object.DestroyImmediate(shell); }
            shell = null;
            renderers.Clear();
        }
    }
}
