// SeekerMine.cs
// Remake (2026-11-05, after Galaxy on Fire 3's boss mine layers): a mine a Most Wanted criminal's "mineLayer" device lays
// (World.NpcDevices, Flight.NpcDeviceSpec its numbers). GoF3's MovingBomb (Mine_04_A01_UB01_D'rak): it chases the player
// within detectionRadius (onlyChasePlayer) at BasicMoveToTarget's speed, goes off within triggerExplosionRadius, or when its
// Lifetime runs out, blasting damageRadiusOnExplosion for damageAmountOnExplosion; its Hull lets the player shoot it down
// (a Target of its own, which NPCs leave alone). It hurts the player's ship only, as the rifts' mines do (RiftHazards.
// BlastPlayer), and slows it by the layer's AbilitySlowdown. Drawn as a red see-through sphere, blinking while it chases.

using GoF2Remake.Flight;
using GoF2Remake.Visuals;
using UnityEngine;

namespace GoF2Remake.World
{
    public class SeekerMine : MonoBehaviour
    {
        const float M = 0.05f;
        const float BodyM = 6f, HitM = 10f;

        SpaceLevel level;
        NpcDeviceSpec spec;
        Target target;
        Renderer body;
        float ageMs;

        public static SeekerMine Lay(SpaceLevel level, NpcDeviceSpec spec, Vector3 at)
        {
            var go = RiftHazards.Sphere(null, at, BodyM, new Color(1f, 0.15f, 0.1f, 0.85f), "Seeker mine");
            var m = go.AddComponent<SeekerMine>();
            m.level = level;
            m.spec = spec;
            m.body = go.GetComponent<Renderer>();
            var t = go.AddComponent<Target>();
            t.maxHp = t.hp = Mathf.Max(1f, spec.hull);
            t.radius = HitM;
            t.hostileToPlayer = true;
            t.untargetable = true;   // (the NPCs' guns leave it alone)
            t.customDeath = true;
            t.displayName = Data.Localization.Extra("seekerMine", "Mine");
            m.target = t;
            return m;
        }

        void Update()
        {
            if (target == null || !target.Alive) { Blast(0.5f); return; }   // (shot down)
            float dt = Time.deltaTime;
            ageMs += dt * 1000f;
            if (ageMs >= spec.lifeMs) { Blast(0.8f); return; }
            var p = RiftHazards.Player(level);
            if (p == null || !p.Alive || p.phased) return;
            var to = p.transform.position - transform.position;
            float d = to.magnitude;
            if (d <= spec.triggerRadius * M) { Blast(0.8f); return; }
            if (d > spec.seekRange * M) { if (body != null) body.enabled = true; return; }
            transform.position += to / d * Mathf.Min(d, spec.seekSpeed * M * dt);
            if (body != null) body.enabled = Mathf.Repeat(ageMs / 150f, 1f) < 0.6f;   // (its warning blink)
        }

        void Blast(float scale)
        {
            Explosion.Spawn(transform.position, scale);
            var p = RiftHazards.Player(level);
            if (p != null && p.Alive && (p.transform.position - transform.position).sqrMagnitude <= spec.blastRadius * M * spec.blastRadius * M)
            {
                RiftHazards.BlastPlayer(level, transform.position, spec.blastRadius * M, spec.damage, false);
                if (spec.slowMs > 0f && spec.slowShare > 0f) p.SlowBy?.Invoke(spec.slowMs, 1f - spec.slowShare, false);
            }
            Destroy(gameObject);
        }
    }
}
