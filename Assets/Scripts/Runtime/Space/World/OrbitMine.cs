// OrbitMine.cs
// Remake (2026-11-06, for mods): one mine of an orbit's minefield (World.OrbitMinefield lays them, after EVERSPACE 2's
// BP_ProximityMine_*): still where it was laid, armed after RiftRun.MineArmS, its fuse (RiftRun.MineFuseS, blinking) lit when
// the player's ship comes within RiftRun.MineTriggerM, then a blast of RiftRun.MineBlastM (less towards the edge) and its
// kind's effect on the ship: web, freeze, a corrosion field, or a wider thermal blast. Drawn as the rift's mines are
// (RiftHazards.Sphere), coloured by kind, and gently bobbing. A shot sets it off at once (BP_ProximityMine_Base's Health:
// one hit; World.MinesweeperWatch counts them), and a minesweeper's are marked on the radar ('marked').

using GoF2Remake.Flight;
using UnityEngine;
using Kind = GoF2Remake.Modding.ModWorld.MinefieldSpec.Kind;

namespace GoF2Remake.World
{
    public class OrbitMine : MonoBehaviour
    {
        /// <summary>A web mine's web (BP_ProximityMine_Web's WebDuration 4 s) and a thermal mine's wider blast.</summary>
        public const float WebMs = 4000f, ThermalBlastFactor = 1.5f;

        SpaceLevel level;
        Kind kind;
        Vector3 home;
        float ageS, fuseS = -1f, damage, phase;
        Renderer body;
        Target target;
        public bool Gone { get; private set; }

        void OnDestroy() { Gone = true; if (target != null) Target.RadarObjects.Remove(target); }

        public static OrbitMine Lay(SpaceLevel level, Transform parent, Vector3 at, Kind kind, float damage, bool marked = false)
        {
            var go = RiftHazards.Sphere(parent, at, RiftRun.MineHitM, OrbitMinefield.ColorOf(kind), "Mine " + kind);
            var m = go.AddComponent<OrbitMine>();
            m.level = level; m.kind = kind; m.damage = damage; m.home = at;
            m.phase = Random.value * Mathf.PI * 2f;
            m.body = go.GetComponent<Renderer>();
            var t = m.target = go.AddComponent<Target>();
            t.hp = t.maxHp = 1f;   // (one hit)
            t.radius = RiftRun.MineHitM;
            t.displayName = GoF2Remake.Data.Localization.Extra("mine", "Mine");
            t.plateNameOnly = t.plateNoIcon = true;
            t.Died += _ => { if (m != null && !m.Gone) m.Detonate(); };
            if (marked) Target.RadarObjects.Add(t);
            return m;
        }

        /// <summary>The blast's reach in metres by kind.</summary>
        public static float BlastM(Kind kind) => RiftRun.MineBlastM * (kind == Kind.Thermal ? ThermalBlastFactor : 1f);

        void Update()
        {
            float dt = Time.deltaTime;
            ageS += dt;
            if (fuseS >= 0f)
            {
                fuseS += dt;
                if (body != null) body.enabled = Mathf.Repeat(fuseS * 10f, 1f) < 0.5f;   // (its alert blink)
                if (fuseS < RiftRun.MineFuseS) return;
                Detonate();
                return;
            }
            transform.position = home + Vector3.up * (Mathf.Sin(ageS * 0.6f + phase) * RiftRun.MineHitM * 0.25f);   // (a slow bob)
            var p = RiftHazards.Player(level);
            if (ageS >= RiftRun.MineArmS && p != null && p.Alive && (p.transform.position - transform.position).sqrMagnitude < RiftRun.MineTriggerM * RiftRun.MineTriggerM)
                fuseS = 0f;
        }

        void Detonate()
        {
            if (Gone) return;
            Gone = true;
            var at = transform.position;
            float reach = BlastM(kind);
            Explosion.Spawn(at, kind == Kind.Thermal ? 1.2f : 0.8f);
            RiftHazards.BlastPlayer(level, at, reach, damage, true);
            var p = RiftHazards.Player(level);
            bool hit = p != null && p.Alive && (p.transform.position - at).sqrMagnitude < reach * reach;
            switch (kind)
            {
                case Kind.Web: if (hit) p.SlowBy?.Invoke(WebMs, NpcRoles.WebSpeedFactor, false); break;
                case Kind.Cryo: if (hit) p.SlowBy?.Invoke(Hazards.FrozenMs, Hazards.FrozenFactor, false); break;
                case Kind.Corrosion:
                    // Corrosive Death's small field where it blew, ticking past the shield
                    RiftField.Make(level, transform.parent, RiftField.Kind.Corrosion, at, RiftRun.CorrosionSmallM, RiftRun.CorrosionSmallS,
                                   RiftRun.CorrosionTickSeconds * NpcTables.RaiderGunDps);
                    break;
            }
            Destroy(gameObject);
        }
    }
}
