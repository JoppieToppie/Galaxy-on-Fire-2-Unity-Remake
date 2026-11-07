// NpcCloud.cs
// Remake (2026-11-05, after Galaxy on Fire 3's smart bombs): a cloud a Most Wanted criminal's "cloud" device lays (World.
// NpcDevices, Flight.NpcDeviceSpec its numbers). GoF3's EffectZone (Mudshark's EffectZoneNanobite_Mudshark_Boss_Weapon: a fog
// field, basicEffectZoneRadius 500, Lifetime 12 s, applyBuffsOnEnter "Speed Buff Mudshark" maxLinearVelocityPostFactor -0.5
// while inside; BuffDamageOverTime's damagePercentagePerInterval in the other zones): while the player's ship is in it, it
// keeps 1 - slowShare of its speed and loses hullPercent of its hull a second, past the shield, and shieldPercent of its
// shield (GoF3's sphere mines' BuffShieldDamageOverTime). The player's ship only, as the rifts' fields (RiftField); drawn
// as a see-through sphere that fades in and out.

using GoF2Remake.Flight;
using UnityEngine;

namespace GoF2Remake.World
{
    public class NpcCloud : MonoBehaviour
    {
        const float M = 0.05f;
        const float FadeMs = 800f, SlowRefreshMs = 250f, TickMs = 1000f;
        static readonly Color Look = new Color(0.55f, 0.85f, 0.35f, 0.2f);

        SpaceLevel level;
        NpcDeviceSpec spec;
        Renderer body;
        float ageMs, tickMs;

        public static NpcCloud Lay(SpaceLevel level, NpcDeviceSpec spec, Vector3 at)
        {
            var go = RiftHazards.Sphere(null, at, spec.cloudRadius * M, Look, "Boss cloud");
            var c = go.AddComponent<NpcCloud>();
            c.level = level;
            c.spec = spec;
            c.body = go.GetComponent<Renderer>();
            return c;
        }

        void Update()
        {
            float dtMs = Time.deltaTime * 1000f;
            ageMs += dtMs;
            if (ageMs >= spec.lifeMs) { Destroy(gameObject); return; }
            if (body != null)
            {
                float a = Mathf.Min(1f, ageMs / FadeMs, (spec.lifeMs - ageMs) / FadeMs);
                body.material.color = new Color(Look.r, Look.g, Look.b, Look.a * a);
            }
            var p = RiftHazards.Player(level);
            float r = spec.cloudRadius * M;
            if (p == null || !p.Alive || p.phased || (p.transform.position - transform.position).sqrMagnitude > r * r) { tickMs = 0f; return; }
            if (spec.slowShare > 0f) p.SlowBy?.Invoke(SlowRefreshMs, 1f - spec.slowShare, false);   // (kept up while inside)
            var hp = p.hitpoints;
            if (spec.shieldPercent > 0f && hp != null && hp.shield > 0f)   // (GoF3's sphere mines: the shield alone)
                hp.shield = Mathf.Max(0f, hp.shield - hp.maxShield * spec.shieldPercent / 100f * dtMs / 1000f);
            if (spec.hullPercent > 0f && (tickMs += dtMs) >= TickMs)
            {
                tickMs -= TickMs;
                p.Damage(p.maxHp * spec.hullPercent / 100f, true, Vector3.zero, 0f);
            }
        }
    }
}
