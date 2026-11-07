// ShotTrigger.cs
// Remake (2026-11-06, for mods): one shot trigger of a secure container (stations.json "orbit.containers.secure",
// World.LootContainer's locked ones), after EVERSPACE 2's BP_ShotTrigger: a small light the player's weapons light by a hit
// (its Health's DamageLimit: it takes no damage, only notices), flickering red while dark and steady green while lit, with
// the rules in Flight.ShotTriggerChain (order, re-darkening). No marker or lock (ES2's has no HUD marker: part of the
// puzzle is finding them); drawn as the rift's spheres (RiftHazards.Sphere), its size the ES2 ships' 3.6 x its 2 m.

using GoF2Remake.Flight;
using UnityEngine;

namespace GoF2Remake.World
{
    public class ShotTrigger : MonoBehaviour
    {
        public const float RadiusM = 2f * 3.6f;
        static readonly Color Dark = new Color(2.5f, 0.15f, 0.1f, 0.9f), Lit = new Color(0.2f, 2.5f, 0.3f, 0.9f);

        ShotTriggerChain chain;
        int index;
        LootContainer container;
        Renderer body;
        float ageS;

        public static ShotTrigger Make(Transform parent, Vector3 at, ShotTriggerChain chain, int index, LootContainer container)
        {
            var go = RiftHazards.Sphere(parent, at, RadiusM, Dark, "Shot trigger " + index);
            var t = go.AddComponent<ShotTrigger>();
            t.chain = chain; t.index = index; t.container = container;
            t.body = go.GetComponent<Renderer>();
            var target = go.AddComponent<Target>();
            target.hp = target.maxHp = 1e6f;
            target.radius = RadiusM;
            target.plateNameOnly = target.plateNoIcon = true;
            target.HitFilter = (amount, dir) => 0f;   // (it takes no damage)
            target.Damaged += (_, __, byNpc) => { if (!byNpc) t.OnShot(); };
            return t;
        }

        void OnShot()
        {
            if (chain == null || chain.IsLit(index)) return;
            if (chain.Hit(index) && container != null) container.CheckUnlock();
        }

        void Update()
        {
            if (chain == null || body == null) return;
            ageS += Time.deltaTime;
            bool lit = chain.IsLit(index);
            // dark: MI_Blockout_Glow_Red_Flicker; lit: MI_Blockout_Glow_Green
            var c = lit ? Lit : Dark * (Mathf.PerlinNoise(ageS * 8f, index) > 0.35f ? 1f : 0.35f);
            c.a = 0.9f;
            body.material.color = c;
        }
    }
}
