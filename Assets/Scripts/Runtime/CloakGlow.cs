// CloakGlow.cs
// The cloak's effect on a ship's non-hull layers (PlayerEgo::update's cloak block 0xa941a..: TransformSetColor of the ship
// group's lights child (+0x10) and engine child (+0x14) with 0xffffff00 | max(50, 255 - fade · 255), and the engine child
// hidden from 25 %). The colour is white with that alpha, so the additive layers (ONE / ONE) keep their full brightness and
// their animations: the ship's lights go on blinking on the vanishing hull. Only the alpha-blended layers dim, to 50 / 255.
// The engine glow (`*_engine_glow_add` / `*_engine_add`) hides between 25 % and 100 %. #47: the remake hid every glow
// layer from 25 %, so a cloaked ship lost its light animations. Shared by PlayerCloak and NpcCloak.
// #85: the original's cloaked player ships keep their engine glow (the reporter's screenshots of a cloaked VoidX and Phantom:
// the glow on the vanished hull; the +0x14 child the code hides is the last child added to the ship group, evidently not the
// glow by then), so the players' ships keep it (KeepEngine); the NPC Specters still hide theirs.

using System.Collections.Generic;
using UnityEngine;

namespace GoF2Remake.Flight
{
    public sealed class CloakGlow
    {
        static readonly int ColorId = Shader.PropertyToID("_Color");

        readonly List<Renderer> engine = new List<Renderer>();
        readonly List<(Renderer r, Color baseColor)> dimmed = new List<(Renderer, Color)>();
        MaterialPropertyBlock block;
        bool engineHidden, dimmedSet;
        /// <summary>The engine glow stays on while cloaked (the players' ships, #85).</summary>
        public bool KeepEngine;

        /// <summary>A non-hull renderer of the ship (not lit by the hull shader).</summary>
        public void Add(Renderer r, Transform root)
        {
            if (r == null) return;
            if (IsEngine(r.transform, root)) { engine.Add(r); return; }
            var m = r.sharedMaterial;
            if (m == null || m.shader == null || !m.HasProperty(ColorId) || m.shader.name.Contains("Additive")) return;
            dimmed.Add((r, m.GetColor(ColorId)));
        }

        static bool IsEngine(Transform t, Transform root)
        {
            for (; t != null; t = t.parent)
            {
                if (t.name.Contains("_engine")) return true;
                if (t == root) break;
            }
            return false;
        }

        /// <summary>The look at 'pct' (0..100) of the cloak's fade.</summary>
        public void Apply(float pct)
        {
            bool hide = pct >= 25f && !KeepEngine;
            if (hide != engineHidden)
            {
                engineHidden = hide;
                foreach (var r in engine) if (r != null) r.forceRenderingOff = hide;
            }
            if (dimmed.Count == 0) return;
            float a = Mathf.Max(50f / 255f, 1f - pct / 100f);
            block ??= new MaterialPropertyBlock();
            foreach (var (r, c) in dimmed)
            {
                if (r == null) continue;
                r.GetPropertyBlock(block);
                block.SetColor(ColorId, new Color(c.r, c.g, c.b, c.a * a));
                r.SetPropertyBlock(block);
            }
            dimmedSet = true;
        }

        /// <summary>Uncloaked: everything shown at its own colour again.</summary>
        public void Restore()
        {
            if (engineHidden) { engineHidden = false; foreach (var r in engine) if (r != null) r.forceRenderingOff = false; }
            if (!dimmedSet) return;
            dimmedSet = false;
            block ??= new MaterialPropertyBlock();
            foreach (var (r, c) in dimmed)
            {
                if (r == null) continue;
                r.GetPropertyBlock(block);
                block.SetColor(ColorId, c);
                r.SetPropertyBlock(block);
            }
        }
    }
}
