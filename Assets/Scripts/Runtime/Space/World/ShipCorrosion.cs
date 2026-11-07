// ShipCorrosion.cs
// Remake (2026-11-06, for mods): a corrosion field's hold on the NPC ships in it (beside World.RiftField's on the player), for
// EVERSPACE 2's acid: its BP_CorrosionField gives the corroded condition to anything inside, not only the player. Every
// RiftRun.CorrosionTickS each NPC ship within the radius takes 'tick' past its shield (the rift field's numbers), while the
// field lasts. Used by World.AcidSackBase's spit and the burst of a thrown acid pouch (World.Portable kind AcidSack).

using GoF2Remake.Flight;
using UnityEngine;

namespace GoF2Remake.World
{
    public class ShipCorrosion : MonoBehaviour
    {
        float radius, lifeS, tick, ageS, tickS;

        public static ShipCorrosion Add(GameObject field, float radiusM, float lifeS, float tick)
        {
            var c = field.AddComponent<ShipCorrosion>();
            c.radius = radiusM; c.lifeS = lifeS; c.tick = tick;
            return c;
        }

        /// <summary>Whether a target is one it corrodes: an NPC ship, alive, inside.</summary>
        public static bool Corrodes(Target t, Vector3 centre, float radiusM) =>
            t != null && !t.isPlayer && t.isShip && t.Alive && (t.transform.position - centre).sqrMagnitude < radiusM * radiusM;

        void Update()
        {
            float dt = Time.deltaTime;
            if ((ageS += dt) > lifeS) { Destroy(this); return; }
            if ((tickS += dt) < RiftRun.CorrosionTickS) return;
            tickS -= RiftRun.CorrosionTickS;
            var at = transform.position;
            for (int i = Target.All.Count - 1; i >= 0; i--)
            {
                var t = Target.All[i];
                if (Corrodes(t, at, radius)) t.Damage(tick, true, Vector3.zero, 0f);
            }
        }
    }
}
