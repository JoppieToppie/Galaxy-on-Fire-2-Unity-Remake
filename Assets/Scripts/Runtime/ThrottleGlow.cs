// ThrottleGlow.cs
// Remake-only: a custom ship's glow that follows its engines (custom_ships.json "throttleGlow", built by
// CustomShipBuilder.BuildThrottleGlow) instead of the exhaust flame, e.g. the USS Enterprise's (65) blue warp nacelle
// grilles. Sits on the additive glow mesh (GoF2/Additive) and sets its _Glow / _Color per frame:
//   throttle 0 -> 'idle', throttle 100 % -> 'full', boosting -> up to 'boost' (FlightModel.BoostVisualPercent);
//   the brake (FlightModel.Braking) counts as throttle 0.
// The player's ship reads its ShipController; any other copy (NPC, another player's ship in multiplayer, the hangar)
// has no throttle at hand, so it uses how fast the ship actually moves against the base speed every ship shares
// (FlightModel.BaseSpeed, game units per ms; above it = boosting). The level eases toward the target so throttle steps
// fade in and out. Hidden with the rest of the player's engine parts (AssembledObject.playerVariantParts).

using GoF2Remake.Flight;
using UnityEngine;

namespace GoF2Remake.Visuals
{
    [Unity.Scripting.LifecycleManagement.NoAutoStaticsCleanup]
    public class ThrottleGlow : MonoBehaviour
    {
        const float MetersPerUnit = 0.05f;

        [Tooltip("Tint (multiplies the mask's own colours).")]
        public Color color = new Color(0.55f, 0.8f, 1f, 1f);
        [Tooltip("Glow intensity at throttle 0.")]
        public float idle = 0.35f;
        [Tooltip("Glow intensity at full throttle (> 1 blooms).")]
        public float full = 4f;
        [Tooltip("Glow intensity at the height of a boost.")]
        public float boost = 7f;
        [Tooltip("How fast the glow follows the throttle (1/s).")]
        public float response = 4f;

        static readonly int GlowId = Shader.PropertyToID("_Glow");
        static readonly int ColorId = Shader.PropertyToID("_Color");

        Renderer[] renderers;
        MaterialPropertyBlock block;
        ShipController ship;
        float level = -1f;
        Vector3 lastPosition;
        bool hasLast;
        float measured;   // the moved-speed estimate (0..2: 1 = base speed)

        void OnEnable()
        {
            renderers = GetComponentsInChildren<Renderer>(true);
            block ??= new MaterialPropertyBlock();
            ship = GetComponentInParent<ShipController>();
            hasLast = false;
        }

        /// <summary>0 = engines idle, 1 = full throttle, up to 2 = full boost.</summary>
        float Drive(float dt)
        {
            if (ship != null && ship.Model != null)
            {
                var m = ship.Model;
                return (m.Braking ? 0f : m.Throttle) + m.BoostVisualPercent;
            }
            var p = transform.position;
            if (hasLast && dt > 0f)
            {
                float unitsPerMs = (p - lastPosition).magnitude / dt / MetersPerUnit / 1000f;
                // A jump or respawn moves the ship in one frame: ignore it.
                if (unitsPerMs < FlightModel.BaseSpeed * 6f)
                    measured = Mathf.Lerp(measured, Mathf.Clamp(unitsPerMs / FlightModel.BaseSpeed, 0f, 2f), 1f - Mathf.Exp(-dt * 5f));
            }
            lastPosition = p;
            hasLast = true;
            return measured;
        }

        void LateUpdate()
        {
            float dt = Time.deltaTime;
            float d = Drive(dt);
            float target = d <= 1f ? Mathf.Lerp(idle, full, d) : Mathf.Lerp(full, boost, d - 1f);
            level = level < 0f ? target : Mathf.Lerp(level, target, 1f - Mathf.Exp(-dt * response));
            block.SetFloat(GlowId, level);
            block.SetColor(ColorId, color);
            foreach (var r in renderers) if (r != null) r.SetPropertyBlock(block);
        }
    }
}
