// Headlights.cs
// Remake (2026-11-02): the player's headlights, after EVERSPACE 2's (decompiled: BP_Ship_Player's HeadLight and
// HeadLightBounceLight components, its VFX_ToggleHeadlights and UpdateHeadLightBounce in the ubergraph; BP_PawnBase's
// VFX_UpdateHeadlights). A key (Options > Controls "Headlights", L) switches them, on by default as in EVERSPACE 2 (its
// player data's bHeadlightsEnabled; its HUD shows a "headlights off" icon while they are off), kept in Settings.Headlights.
//   The spotlight (HeadLight): one spot light at the nose, ahead of the ship by its bounding sphere's radius and down at
//   the bottom of its bounds (BP_Ship_Player: relative X = SphereRadius, Z = -BoxExtent.Z), inner cone 20 deg, outer 22 deg,
//   reach 50 000 cm, intensity 1 with the Star_Burst4 IES profile and a falloff exponent of 4, no shadows. Here: the same
//   cone, the reach in the ship's scale (500 m x the kit's scale), URP's intensity a tunable (Intensity; the exposures
//   differ and the IES profile isn't shipped), no shadows.
//   The bounce light (HeadLightBounceLight, VFX quality 3 and up): a point light at the spot the headlight's ray hits
//   (a line trace along the headlight's forward over its reach); its soft source radius eased (4/s) to the cone's radius
//   there, sqrt((d / cos 22 deg)^2 - d^2), its reach to 4 x that, its position eased (4/s) to the hit, its intensity eased
//   (2/s) to the headlight's falloff there (LIB_F_General.CalculateLightIntensityFalloff(reach, 10, exponent 2, from, to): not
//   dumped, taken as Unreal's radial attenuation, base x (1 - (d / reach)^2)^exponent); nothing hit: eased (4/s) to 0.
//   Here the ray asks the hull colliders (HullCollision: stations, gates, freighters, the big ships), not the asteroids'
//   cubes, which have no colliders.
//   The hull's own lights: EVERSPACE 2 sets every pawn mesh's "Headlight" material scalar 1 / 0 with the toggle and runs a
//   lens flare at each SKT_HeadLight socket (BP_PawnHeadlight); a mod ship with those (the ES2 importer's kits) gets them
//   from Modding.Es2Headlights, which reads On here.
// Off while docked (the hangar) and once the ship is dead. Not in EVERSPACE 2's first-person view (the remake has none).

using GoF2Remake.Data;
using UnityEngine;

namespace GoF2Remake.Flight
{
    [Unity.Scripting.LifecycleManagement.NoAutoStaticsCleanup]
    public class Headlights : MonoBehaviour
    {
        public const float InnerConeDeg = 20f, OuterConeDeg = 22f;
        public const float ReachEs2 = 500f;             // 50 000 cm
        const float BounceBase = 10f, BounceExponent = 2f;
        const float EaseRadius = 4f, EasePosition = 4f, EaseIntensity = 2f, EaseOff = 4f;
        /// <summary>URP intensities for EVERSPACE 2's unitless 1 (the spot) and 10 (the bounce): the exposures differ. Not
        /// matched by eye yet (set headless); the Debug panel's eval can tune them.</summary>
        public static float Intensity = 3000f, BounceGain = 40f;

        /// <summary>The headlights are switched on (Settings.Headlights, EVERSPACE 2's bHeadlightsEnabled).</summary>
        public static bool On
        {
            get => Settings.Headlights;
            set => Settings.Headlights = value;
        }

        ShipController ship;
        PlayerHealth health;
        Light spot, bounce;
        Transform spotModel;
        float es2Scale = 1f;
        bool inStation;

        public static Headlights Attach(GameObject player)
        {
            var h = player.GetComponent<Headlights>();
            if (h == null) h = player.AddComponent<Headlights>();
            return h;
        }

        /// <summary>The cone's radius at distance 'd' from a spotlight of outer cone angle 'coneDeg' (EVERSPACE 2's
        /// sqrt((d / cos)^2 - d^2) = d tan).</summary>
        public static float ConeRadius(float d, float coneDeg = OuterConeDeg)
        {
            float c = Mathf.Cos(coneDeg * Mathf.Deg2Rad);
            float h = d / Mathf.Max(c, 1e-4f);
            return Mathf.Sqrt(Mathf.Max(0f, h * h - d * d));
        }

        /// <summary>A light's intensity at distance 'd' with reach 'reach', its base intensity and falloff exponent (Unreal's
        /// radial attenuation without inverse-square falloff: base x (1 - (d / reach)^2)^exponent, 0 beyond the reach).</summary>
        public static float Falloff(float reach, float baseIntensity, float exponent, float d)
        {
            if (reach <= 0f || d >= reach) return 0f;
            float k = 1f - (d * d) / (reach * reach);
            return baseIntensity * Mathf.Pow(Mathf.Max(0f, k), exponent);
        }

        /// <summary>Unreal's FInterpTo: toward the target by speed x dt of the distance, never past it.</summary>
        public static float InterpTo(float current, float target, float dt, float speed) =>
            speed <= 0f ? target : Mathf.Lerp(current, target, Mathf.Clamp01(dt * speed));

        void Awake()
        {
            ship = GetComponent<ShipController>();
            health = GetComponent<PlayerHealth>();
        }

        void OnDisable() { if (spot != null) spot.enabled = false; if (bounce != null) bounce.enabled = false; }

        void OnDestroy() { if (bounce != null) Destroy(bounce.gameObject); }

        void Update()
        {
            bool live = ship == null || (ship.useBuiltInInput && !Navigation.InputHalted);
            if (live && GameControls.Headlights != null && GameControls.Headlights.WasPressedThisFrame()) On = !On;
            var model = ship != null ? ship.visualModel : null;
            bool on = On && model != null && (health == null || !health.Dead);
            if (on && spotModel != model) Place(model);
            if (!on) { if (spot != null) spot.enabled = false; FadeBounce(Time.deltaTime, true); return; }
            spot.enabled = true;
            UpdateBounce(Time.deltaTime);
        }

        /// <summary>The spotlight at the nose of this model (made once per model): its bounds in the ship's frame.</summary>
        void Place(Transform model)
        {
            spotModel = model;
            inStation = Visuals.EngineDrive.IsStationModel(model);
            es2Scale = Mathf.Max(1f, model.localScale.x);
            var b = new Bounds(Vector3.zero, Vector3.zero);
            bool any = false;
            foreach (var mr in model.GetComponentsInChildren<MeshRenderer>())
            {
                var wb = mr.bounds;
                for (int i = 0; i < 8; i++)
                {
                    var corner = new Vector3((i & 1) == 0 ? wb.min.x : wb.max.x, (i & 2) == 0 ? wb.min.y : wb.max.y, (i & 4) == 0 ? wb.min.z : wb.max.z);
                    var local = transform.InverseTransformPoint(corner);
                    if (!any) { b = new Bounds(local, Vector3.zero); any = true; } else b.Encapsulate(local);
                }
            }
            if (spot == null)
            {
                var go = new GameObject("headlight");
                go.transform.SetParent(transform, false);
                spot = go.AddComponent<Light>();
                spot.type = LightType.Spot;
                spot.shadows = LightShadows.None;
                spot.color = Color.white;
                var bgo = new GameObject("headlight_bounce");
                bounce = bgo.AddComponent<Light>();
                bounce.type = LightType.Point;
                bounce.shadows = LightShadows.None;
                bounce.color = Color.white;
                bounce.intensity = 0f;
                bounce.enabled = false;
            }
            spot.transform.localPosition = new Vector3(0f, b.min.y, b.center.z + b.extents.magnitude);
            spot.transform.localRotation = Quaternion.identity;
            spot.spotAngle = OuterConeDeg * 2f;
            spot.innerSpotAngle = InnerConeDeg * 2f;
            spot.range = ReachEs2 * es2Scale;
            spot.intensity = Intensity;
        }

        float bounceRadius;

        void FadeBounce(float dt, bool off)
        {
            if (bounce == null) return;
            bounce.intensity = off ? 0f : InterpTo(bounce.intensity, 0f, dt, EaseOff);
            bounce.enabled = bounce.intensity > 0.01f;
        }

        void UpdateBounce(float dt)
        {
            if (inStation) { FadeBounce(dt, true); return; }
            var from = spot.transform.position;
            var dir = spot.transform.forward;
            float reach = spot.range;
            if (Physics.Raycast(from, dir, out var hit, reach, HullCollision.HullMask, QueryTriggerInteraction.Ignore) && !hit.transform.IsChildOf(transform))
            {
                float d = hit.distance;
                bounceRadius = InterpTo(bounceRadius, ConeRadius(d), dt, EaseRadius);
                bounce.range = InterpTo(bounce.range, bounceRadius * 4f, dt, EaseRadius);
                var pos = bounce.enabled ? bounce.transform.position : hit.point;
                bounce.transform.position = Vector3.Lerp(pos, hit.point, Mathf.Clamp01(dt * EasePosition));
                float target = Falloff(reach, BounceBase, BounceExponent, Vector3.Distance(from, bounce.transform.position)) * BounceGain;
                bounce.intensity = InterpTo(bounce.intensity, target, dt, EaseIntensity);
                bounce.enabled = bounce.intensity > 0.01f;
            }
            else FadeBounce(dt, false);
        }
    }
}
