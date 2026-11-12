// RiftHazards.cs
// Remake (2026-11-04, for mods): the things a rift's newer mutators put in space (World.RiftEncounter runs them, Flight.RiftRun
// has their ES2 numbers): mines from the dead (Mines on Death), corrosion fields (Corrosive Death), burst bulbs, web bubbles
// (Stasis Snares) and safe fields (Apocalypse). They hurt or slow the player's ship only, as ES2's do (its mines, fields and
// bubbles are the rift's enemies' side), and go with the rift (children of its object). Drawn as plain see-through spheres
// (ES2's own effects are its assets): red mines, green corrosion, an orange bulb, violet bubbles, a blue safe field.

using GoF2Remake.Flight;
using UnityEngine;

namespace GoF2Remake.World
{
    public static class RiftHazards
    {
        static Material look;

        /// <summary>A see-through sphere of 'radius' metres in 'color' under 'parent' (no collider).</summary>
        public static GameObject Sphere(Transform parent, Vector3 at, float radius, Color color, string name)
        {
            var go = GameObject.CreatePrimitive(PrimitiveType.Sphere);
            go.name = name;
            Object.Destroy(go.GetComponent<Collider>());
            go.transform.SetParent(parent, false);
            go.transform.position = at;
            go.transform.localScale = Vector3.one * radius * 2f;
            var r = go.GetComponent<Renderer>();
            if (look == null)
            {
                var shader = Shader.Find("Sprites/Default") ?? Shader.Find("Universal Render Pipeline/Unlit");
                if (shader != null) look = new Material(shader) { name = "Rift hazard" };
            }
            if (look != null) { r.sharedMaterial = look; r.material.color = color; }
            r.shadowCastingMode = UnityEngine.Rendering.ShadowCastingMode.Off;
            r.receiveShadows = false;
            return go;
        }

        /// <summary>The local player's ship's target, null without one.</summary>
        public static Target Player(SpaceLevel level) => level != null && level.Player != null ? level.Player.GetComponent<Target>() : null;

        /// <summary>The player's ship within 'radius' metres of 'at' takes 'damage', less towards the edge when 'falloff'.</summary>
        public static void BlastPlayer(SpaceLevel level, Vector3 at, float radius, float damage, bool falloff, float shieldFactor = 1f)
        {
            var p = Player(level);
            if (p == null || !p.Alive || damage <= 0f) return;
            var d = p.transform.position - at;
            float r = d.magnitude;
            if (r > radius) return;
            p.Damage(falloff ? damage * (1f - r / radius) : damage, true, d.normalized, shieldFactor);
        }
    }

    /// <summary>Mines on Death: a proximity mine drifting from where an enemy died (RiftRun.Mine*).</summary>
    public class RiftMine : MonoBehaviour
    {
        SpaceLevel level;
        Vector3 velocity;
        float ageS, lifeS, fuseS = -1f, damage;
        Renderer body;

        public static RiftMine Drop(SpaceLevel level, Transform parent, Vector3 at, Vector3 heading, float damage)
        {
            var dir = Quaternion.AngleAxis(Random.Range(0f, 360f), heading) * Quaternion.AngleAxis(Random.Range(0f, RiftRun.MineConeDeg), Vector3.Cross(heading, Random.onUnitSphere).normalized) * heading;
            var go = RiftHazards.Sphere(parent, at + heading * RiftRun.MineAheadM, RiftRun.MineHitM, new Color(1f, 0.1f, 0.1f, 0.8f), "Rift mine");
            var m = go.AddComponent<RiftMine>();
            m.level = level;
            m.velocity = dir.normalized * RiftRun.MineFlingMs;
            m.lifeS = Random.Range(RiftRun.MineLifeMinS, RiftRun.MineLifeMaxS);
            m.damage = damage;
            m.body = go.GetComponent<Renderer>();
            return m;
        }

        void Update()
        {
            float dt = Time.deltaTime;
            ageS += dt;
            transform.position += velocity * dt;
            velocity *= Mathf.Exp(-RiftRun.MineDamping * dt);   // (UE's linear damping)
            if (fuseS >= 0f)
            {
                fuseS += dt;
                if (body != null) body.enabled = Mathf.Repeat(fuseS * 10f, 1f) < 0.5f;   // (its alert blink)
                if (fuseS < RiftRun.MineFuseS) return;
                Explosion.Spawn(transform.position, 0.8f);
                RiftHazards.BlastPlayer(level, transform.position, RiftRun.MineBlastM, damage, true);
                Destroy(gameObject);
                return;
            }
            if (ageS > lifeS) { Destroy(gameObject); return; }
            var p = RiftHazards.Player(level);
            if (ageS >= RiftRun.MineArmS && p != null && p.Alive && (p.transform.position - transform.position).sqrMagnitude < RiftRun.MineTriggerM * RiftRun.MineTriggerM)
                fuseS = 0f;
        }
    }

    /// <summary>A sphere the player's ship is in or out of: a corrosion field (corrodes it), a web bubble (webs it and pops)
    /// or a safe field (Apocalypse: after its build-up the ship can't be harmed while in it).</summary>
    public class RiftField : MonoBehaviour
    {
        public enum Kind { Corrosion, Web, Safe }

        SpaceLevel level;
        Kind kind;
        float radius, lifeS, ageS, insideS, tickS, corrodedS, tickDamage;
        public bool Protecting { get; private set; }

        public static RiftField Make(SpaceLevel level, Transform parent, Kind kind, Vector3 at, float radius, float lifeS, float tickDamage = 0f)
        {
            var color = kind == Kind.Corrosion ? new Color(0.4f, 1f, 0.2f, 0.18f) : kind == Kind.Web ? new Color(0.7f, 0.4f, 1f, 0.22f) : new Color(0.3f, 0.7f, 1f, 0.15f);
            var go = RiftHazards.Sphere(parent, at, radius, color, "Rift " + kind);
            var f = go.AddComponent<RiftField>();
            f.level = level; f.kind = kind; f.radius = radius; f.lifeS = lifeS; f.tickDamage = tickDamage;
            return f;
        }

        public bool Contains(Vector3 p) => (p - transform.position).sqrMagnitude < radius * radius;

        System.Func<bool> corroded;

        void Start()
        {
            if (kind != Kind.Corrosion) return;
            corroded = () => corrodedS > 0f;
            PlayerDebuffs.Register(corroded, () => corrodedS = 0f);   // (a System Recovery Routine lifts it)
        }

        void OnDestroy() { if (corroded != null) PlayerDebuffs.Unregister(corroded); }

        void Update()
        {
            float dt = Time.deltaTime;
            ageS += dt;
            var p = RiftHazards.Player(level);
            bool inside = p != null && p.Alive && Contains(p.transform.position);
            switch (kind)
            {
                case Kind.Corrosion:
                    // inside: corroded for CorrosionDebuffS more, a tick every CorrosionTickS past the shield
                    if (inside) corrodedS = RiftRun.CorrosionDebuffS;
                    if (corrodedS > 0f && p != null)
                    {
                        corrodedS -= dt;
                        if ((tickS += dt) >= RiftRun.CorrosionTickS) { tickS -= RiftRun.CorrosionTickS; p.Damage(tickDamage, true, Vector3.zero, 0f); }
                    }
                    if (ageS > lifeS && corrodedS <= 0f) Destroy(gameObject);
                    else if (ageS > lifeS) { var r = GetComponent<Renderer>(); if (r != null) r.enabled = false; }
                    break;
                case Kind.Web:
                    if (inside)
                    {
                        p.SlowBy?.Invoke(RiftRun.SnareWebS * 1000f, NpcRoles.WebSpeedFactor, false);
                        Explosion.Spawn(transform.position, 0.3f, false);
                        Destroy(gameObject);
                    }
                    break;
                case Kind.Safe:
                    insideS = inside ? insideS + dt : 0f;
                    Protecting = inside && insideS >= RiftRun.SafeBuildupS;
                    if (lifeS > 0f && ageS > lifeS) Destroy(gameObject);
                    break;
            }
        }
    }

    /// <summary>Burst Bulbs: a bulb that grows and bursts over the whole stage unless the pilot destroys it first.</summary>
    public class RiftBulb : MonoBehaviour
    {
        SpaceLevel level;
        float ageS, damage;
        public Target Target { get; private set; }

        public static RiftBulb Grow(SpaceLevel level, Transform parent, Vector3 at, float hull, float damage)
        {
            var go = RiftHazards.Sphere(parent, at, RiftRun.BulbStartM, new Color(1f, 0.55f, 0.1f, 0.6f), "Burst bulb");
            var b = go.AddComponent<RiftBulb>();
            b.level = level; b.damage = damage;
            // shootable by the player only (OnlyDestructibleByPlayer): a radar object of its own, no wreck
            var t = go.AddComponent<Target>();
            t.maxHp = t.hp = Mathf.Max(1f, hull);
            t.radius = RiftRun.BulbStartM;
            t.hostileToPlayer = true;
            t.customDeath = true;
            t.displayName = Data.Localization.Extra("burstBulb", "Burst Bulb");
            t.plateNameOnly = t.plateNoIcon = true;
            b.Target = t;
            Target.RadarObjects.Add(t);   // (its marker and lock, as a loot container's)
            return b;
        }

        void OnDestroy() { if (Target != null) Target.RadarObjects.Remove(Target); }

        void Update()
        {
            if (Target == null || !Target.Alive) { Explosion.Spawn(transform.position, 0.6f); Destroy(gameObject); return; }   // (shot down)
            ageS += Time.deltaTime;
            float r = RiftRun.BulbStartM * Mathf.Lerp(1f, RiftRun.BulbGrowth, Mathf.Clamp01(ageS / RiftRun.BulbLifeS));
            transform.localScale = Vector3.one * r * 2f;
            Target.radius = r;
            if (ageS < RiftRun.BulbLifeS) return;
            Explosion.Spawn(transform.position, 3f);
            RiftHazards.BlastPlayer(level, transform.position, RiftRun.BulbBurstM, damage, false);
            Destroy(gameObject);
        }
    }
}
