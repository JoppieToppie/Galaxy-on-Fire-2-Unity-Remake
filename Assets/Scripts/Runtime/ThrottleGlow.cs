// ThrottleGlow.cs
// Remake-only: a custom ship's glow that follows its engines (custom_ships.json "throttleGlow", built by
// CustomShipBuilder.BuildThrottleGlow) instead of the exhaust flame, e.g. the USS Enterprise's (65) blue warp nacelle
// grilles. Sits on the additive glow mesh (GoF2/Additive) and sets its _Glow / _Color per frame:
//   throttle 0 -> 'idle', throttle 100 % -> 'full', boosting -> up to 'boost' (FlightModel.BoostVisualPercent);
//   the brake (FlightModel.Braking) counts as throttle 0.
// Travelling (the planet jump: Navigation.Jumping; a jumpgate or Khador jump: SystemJump.Traveling) counts as a full boost,
// and the Khador Drive's charge builds the glow up toward it. While boosting or travelling, an optional trail in the glow's
// colour streams from the glow's rear ends ('trailPoints', set by the builder; 'trailWidth' metres, 'trailTime' s; a band's
// trails, 'trailProfile', shrink and dim from its middle to its ends like an ellipse); never
// while docking or leaving a station (the hangar flights, the launch fly-in).
// The player's ship reads its ShipController; any other copy (NPC, another player's ship in multiplayer, the hangar)
// has no throttle at hand, so it uses how fast the ship actually moves against the base speed every ship shares
// (FlightModel.BaseSpeed, game units per ms; above it = boosting). The level eases toward the target so throttle steps
// fade in and out. Never hidden with the engines: while the game has the player's engine parts off (mining, object
// docking, cutscenes; AssembledObject.playerVariantParts holds the empty 'engineState' for it) the glow sits at 'idle'.

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
        [Tooltip("An empty player engine part: while the game has the engines off (it is inactive) the glow stays at idle.")]
        public GameObject engineState;
        [Tooltip("Where the trails start (this object's space); none = no trail.")]
        public Vector3[] trailPoints = new Vector3[0];
        [Tooltip("Per trail point (empty = all 1): its width, brightness and (0.4 + 0.6 x) length; the ellipse across a band.")]
        public float[] trailProfile = new float[0];
        [Tooltip("Trail width at its start, metres (0 = no trail).")]
        public float trailWidth;
        [Tooltip("Trail length in seconds.")]
        public float trailTime = 0.6f;
        [Tooltip("Trail brightness, x the glow's level (additive: dimmer also reads as more see-through).")]
        public float trailBrightness = 0.3f;

        static readonly int GlowId = Shader.PropertyToID("_Glow");
        static readonly int ColorId = Shader.PropertyToID("_Color");

        Renderer[] renderers;
        MaterialPropertyBlock block;
        ShipController ship;
        Navigation nav;
        World.SystemJump systemJump;
        TrailRenderer[] trails;
        Material trailMaterial;
        float level = -1f;
        Vector3 lastPosition;
        bool hasLast;
        float lookupTimer;   // the level adds Navigation / SystemJump to the player after spawning the model: look again
        World.SpaceLevel spaceLevel;
        bool inStation;      // the docked station scene: hangar flights (arriving, leaving) never leave a trail
        float measured;   // the moved-speed estimate (0..2: 1 = base speed)

        void OnEnable()
        {
            if (trails == null) BuildTrails();
            renderers = GetComponent<Renderer>() != null ? new[] { GetComponent<Renderer>() } : new Renderer[0];
            block ??= new MaterialPropertyBlock();
            ship = GetComponentInParent<ShipController>();
            nav = GetComponentInParent<Navigation>();
            systemJump = GetComponentInParent<World.SystemJump>();
            inStation = FindAnyObjectByType<World.StationLevel>() != null;
            spaceLevel = inStation ? null : FindAnyObjectByType<World.SpaceLevel>();
            hasLast = false;
        }

        /// <summary>No trail while docking or leaving a station: the station scene's hangar flights, and in space the launch
        /// fly-in after leaving one (the arrival after a planet jump / jumpgate / wormhole keeps it).</summary>
        bool TrailSuppressed => inStation
            || (spaceLevel != null && !spaceLevel.LaunchCameraOver && !spaceLevel.StreamOutArrival);

        void OnDestroy()
        {
            if (trailMaterial != null) Destroy(trailMaterial);
        }

        /// <summary>One TrailRenderer per trail point, on a copy of the glow's material that takes vertex colours (the
        /// colour fades to black along the trail: the material is additive).</summary>
        void BuildTrails()
        {
            trails = new TrailRenderer[0];
            var mr = GetComponent<Renderer>();
            if (trailWidth <= 0f || trailPoints == null || trailPoints.Length == 0 || mr == null || mr.sharedMaterial == null) return;
            trailMaterial = new Material(mr.sharedMaterial) { name = mr.sharedMaterial.name + " (trail)" };
            // Plain white along the trail: the glow's own texture is its mask (mostly black outside the lit parts), which
            // stretched over the trail left it nearly invisible.
            trailMaterial.SetTexture("_MainTex", Texture2D.whiteTexture);
            trailMaterial.EnableKeyword("_USEVERTEXCOLOR_ON");
            trailMaterial.SetFloat("_UseVertexColor", 1f);
            trailMaterial.SetColor(ColorId, color);
            trailMaterial.SetFloat(GlowId, full * trailBrightness);
            var gradient = new Gradient();
            gradient.SetKeys(new[] { new GradientColorKey(new Color(0.55f, 0.55f, 0.55f), 0f), new GradientColorKey(new Color(0.22f, 0.22f, 0.22f), 0.3f), new GradientColorKey(Color.black, 1f) },
                             new[] { new GradientAlphaKey(1f, 0f), new GradientAlphaKey(0f, 1f) });
            trails = new TrailRenderer[trailPoints.Length];
            for (int i = 0; i < trailPoints.Length; i++)
            {
                var go = new GameObject("trail_" + i);
                go.transform.SetParent(transform, false);
                go.transform.localPosition = trailPoints[i];
                float p = trailProfile != null && i < trailProfile.Length ? trailProfile[i] : 1f;
                var t = go.AddComponent<TrailRenderer>();
                t.sharedMaterial = trailMaterial;
                t.time = trailTime * (0.4f + 0.6f * p);
                t.minVertexDistance = trailWidth * 0.5f;
                t.widthMultiplier = trailWidth * p;
                t.widthCurve = AnimationCurve.EaseInOut(0f, 1f, 1f, 0f);
                t.colorGradient = p >= 1f ? gradient : Scaled(gradient, p);
                t.shadowCastingMode = UnityEngine.Rendering.ShadowCastingMode.Off;
                t.receiveShadows = false;
                t.numCapVertices = 2;
                t.emitting = false;
                trails[i] = t;
            }
        }

        /// <summary>The gradient with its colours x 'k' (additive material: dimmer).</summary>
        static Gradient Scaled(Gradient g, float k)
        {
            var keys = g.colorKeys;
            for (int i = 0; i < keys.Length; i++) keys[i].color *= k;
            var scaled = new Gradient();
            scaled.SetKeys(keys, g.alphaKeys);
            return scaled;
        }

        /// <summary>0 = engines idle, 1 = full throttle, up to 2 = full boost.</summary>
        float Drive(float dt)
        {
            if (engineState != null && !engineState.activeInHierarchy) { hasLast = false; measured = 0f; return 0f; }
            // Travelling between planets / systems: a full boost; the Khador Drive's charge builds up to it.
            if ((nav != null && nav.Jumping) || (systemJump != null && systemJump.Traveling)) return 2f;
            if (ship != null && ship.Model != null)
            {
                var m = ship.Model;
                float d = (m.Braking ? 0f : m.Throttle) + m.BoostVisualPercent;
                if (systemJump != null && systemJump.Charging) d = Mathf.Max(d, 1f + systemJump.ChargeRate);
                return d;
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
            if ((nav == null || systemJump == null || ship == null) && (lookupTimer -= Time.unscaledDeltaTime) <= 0f)
            {
                lookupTimer = 0.5f;
                if (ship == null) ship = GetComponentInParent<ShipController>();
                if (nav == null) nav = GetComponentInParent<Navigation>();
                if (systemJump == null) systemJump = GetComponentInParent<World.SystemJump>();
            }
            float d = Drive(dt);
            float target = d <= 1f ? Mathf.Lerp(idle, full, d) : Mathf.Lerp(full, boost, d - 1f);
            level = level < 0f ? target : Mathf.Lerp(level, target, 1f - Mathf.Exp(-dt * response));
            block.SetFloat(GlowId, level);
            block.SetColor(ColorId, color);
            foreach (var r in renderers) if (r != null) r.SetPropertyBlock(block);
            // The trail while boosting or travelling (above full throttle), as bright as the glow.
            if (trails != null && trails.Length > 0)
            {
                bool emit = d > 1.05f && gameObject.activeInHierarchy && !TrailSuppressed;
                if (trailMaterial != null) trailMaterial.SetFloat(GlowId, Mathf.Max(full, level) * trailBrightness);
                foreach (var t in trails) if (t != null && t.emitting != emit) t.emitting = emit;
            }
        }
    }
}
