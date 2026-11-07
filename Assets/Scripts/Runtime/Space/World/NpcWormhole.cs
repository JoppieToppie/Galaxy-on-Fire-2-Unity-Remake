// NpcWormhole.cs
// Remake (2026-11-05, after Galaxy on Fire 3's Dynamic Wormhole Generator): the hole a Most Wanted criminal's "wormhole"
// device opens (World.NpcDevices, Flight.NpcDeviceSpec its numbers). GoF3 (DeviceLogicDynamicWormholeGenerator::start,
// DynamicWormholeSystem; Khador's DynamicWormholeEntity): the hole opens at the owner's (0, 0, 50), behind its tail, for
// its Lifetime; a ship flying within wormholeRadius comes out exitDistanceFromOwner in front of the owner ("this punches a
// hole in space and another in front of the ship firing it. So if a foolish pilot flies into the singularity, they'll end
// up right in the cross hairs"). The remake: the local player's ship only, through SpaceLevel.WarpPlayer, heading as it
// was (the owner now behind it), a flash at both ends; the hole stays where it opened, the exit follows the owner's nose.
// Drawn as a dark violet sphere, a faint one at the exit.

using GoF2Remake.Flight;
using GoF2Remake.Visuals;
using UnityEngine;

namespace GoF2Remake.World
{
    public class NpcWormhole : MonoBehaviour
    {
        const float M = 0.05f;
        const float FadeMs = 400f;
        static readonly Color Mouth = new Color(0.35f, 0.1f, 0.6f, 0.7f), Exit = new Color(0.6f, 0.3f, 1f, 0.2f);

        SpaceLevel level;
        NpcDeviceSpec spec;
        Transform owner;
        Renderer body, exitBody;
        float ageMs;

        public static NpcWormhole Open(SpaceLevel level, NpcDeviceSpec spec, Transform owner)
        {
            var at = owner.position - owner.forward * (spec.layBehind * M);
            var go = RiftHazards.Sphere(null, at, spec.radius * M, Mouth, "Boss wormhole");
            var w = go.AddComponent<NpcWormhole>();
            w.level = level;
            w.spec = spec;
            w.owner = owner;
            w.body = go.GetComponent<Renderer>();
            var exit = RiftHazards.Sphere(null, w.ExitPoint, spec.radius * M, Exit, "Boss wormhole exit");   // (not a child: the mouth is scaled)
            w.exitBody = exit.GetComponent<Renderer>();
            Explosion.Spawn(at, 0.5f, false);
            return w;
        }

        void OnDestroy() { if (exitBody != null) Destroy(exitBody.gameObject); }

        Vector3 ExitPoint => owner.position + owner.forward * (spec.exitDistance * M);

        void Update()
        {
            ageMs += Time.deltaTime * 1000f;
            if (owner == null || ageMs >= spec.lifeMs) { Destroy(gameObject); return; }
            float a = Mathf.Clamp01(Mathf.Min(ageMs / FadeMs, (spec.lifeMs - ageMs) / FadeMs));
            if (body != null) body.material.color = new Color(Mouth.r, Mouth.g, Mouth.b, Mouth.a * a);
            if (exitBody != null)
            {
                exitBody.transform.position = ExitPoint;
                exitBody.material.color = new Color(Exit.r, Exit.g, Exit.b, Exit.a * a);
            }
            var p = RiftHazards.Player(level);
            float r = spec.radius * M;
            if (level == null || p == null || !p.Alive || p.phased || (p.transform.position - transform.position).sqrMagnitude > r * r) return;
            var to = ExitPoint;
            Explosion.Spawn(p.transform.position, 0.5f, false);
            level.WarpPlayer(to, p.transform.rotation);
            Explosion.Spawn(to, 0.5f);
            Destroy(gameObject);
        }
    }
}
