// KhadorTimeline.cs
// Remake: the Khador Slipstream's way in (World.KhadorFx): when each element of the animation shows and how strongly, from the keys of the original fx
// (khador_jump.aem, mesh 15026, its parts' `extra` opacity): the beam 33-2266 ms (parts 1, 3, 10), the focus' star 33-2133 (2),
// the web 500-3100 (4-9) and 700-3033 (11-16), the sparks 600-2000 (22-34), the ring 1066-2366 (18), the flash (21). Plain C#. The web is the
// size the fx's parts reach at the fx's scale of 2 (game units). The ring is the rim of a hole: it opens slowly enough to frame the
// ship flying through, then runs out past the camera. The original's own jump keeps its fx (SystemJump.SpawnKhadorFx).

using UnityEngine;

namespace GoF2Remake.Flight
{
    public static class KhadorTimeline
    {
        public const float LengthMs = 4000f;
        public const float BeamStartMs = 33f, BeamEndMs = 2266f, StarEndMs = 2133f;
        public const float WebStartMs = 500f, WebEndMs = 3100f;
        public const float SparksStartMs = 600f, SparksEndMs = 2000f;
        public const float RingStartMs = 1066f, RingEndMs = 2366f;
        /// <summary>How far the focus lies ahead of the ship (game units; MGame::startJumpScene).</summary>
        public const float FocusUnits = 3000f;
        const float WebUnits = 8000f;

        /// <summary>Into interstellar space the animation stands further ahead, seen from the chase camera as it opens, and runs
        /// slower (its clock at this rate of the scene's): the user, there being a hole to fly into, nothing needs the side view.
        /// The ship eases back while it forms, and from RiftPullMs is drawn in.</summary>
        public const float RiftFocusUnits = 7500f, RiftRate = 0.8f, RiftPullMs = 1300f;
        const float RiftHoldSpeed = 1.5f, RiftDrawnSpeed = 8f, RiftEaseMs = 500f, RiftDrawMs = 1000f;
        /// <summary>The speed the ship goes through at, against the ship's own: the next level's plume starts as this one's.</summary>
        public const float RiftDrawnFactor = RiftDrawnSpeed / 2f;

        /// <summary>The ship's speed (u/ms) on the way into the hole: the scene's 2 u/ms easing back, then drawn in.</summary>
        public static float RiftSpeed(float ms) => ms < RiftPullMs
            ? Mathf.Lerp(2f, RiftHoldSpeed, Mathf.SmoothStep(0f, 1f, ms / RiftEaseMs))
            : Mathf.Lerp(RiftHoldSpeed, RiftDrawnSpeed, Mathf.SmoothStep(0f, 1f, (ms - RiftPullMs) / RiftDrawMs));

        /// <summary>The warp over the orbit left, 0..1: it starts as the ship is drawn in and is full as it reaches the hole, whose
        /// warp it joins, so the orbit's own sky runs into the next level's. 'crossMs' as RiftCrossMs.</summary>
        public static float RiftWarp(float ms, float crossMs) => Mathf.SmoothStep(0f, 1f, Mathf.InverseLerp(RiftPullMs, crossMs, ms));

        /// <summary>When a ship at RiftSpeed has flown 'units' (ms of the scene).</summary>
        public static float RiftCrossMs(float units)
        {
            float ms = 0f;
            while (units > 0f && ms < 5000f) { units -= RiftSpeed(ms) * 10f; ms += 10f; }
            return ms;
        }

        /// <summary>A rise from 0 over 'inMs', held, and a fall to 0 over 'outMs' ending at 'endMs'.</summary>
        static float Pulse(float ms, float startMs, float inMs, float endMs, float outMs) =>
            Mathf.Clamp01((ms - startMs) / inMs) * Mathf.Clamp01((endMs - ms) / outMs);

        /// <summary>The beam: lances out at once, and thins from the ring on.</summary>
        public static float Beam(float ms) => Pulse(ms, BeamStartMs, 130f, BeamEndMs + 1000f, 1500f);
        /// <summary>The beam's width against its full width: it hits at full width, then settles to a bright line.</summary>
        public static float BeamWidth(float ms) => Mathf.Lerp(1f, 0.42f, Mathf.SmoothStep(0f, 1f, Mathf.InverseLerp(180f, 900f, ms)));
        public static float Star(float ms) => Pulse(ms, BeamStartMs, 200f, StarEndMs, 500f);
        /// <summary>The web's opacity, and how far it has grown (0..1 of WebSize).</summary>
        public static float Web(float ms) => Pulse(ms, WebStartMs - 150f, 200f, WebEndMs, 1400f);
        public static float WebGrown(float ms) => 1f - Mathf.Pow(1f - Mathf.Clamp01((ms - WebStartMs + 150f) / 1000f), 2.4f);
        public static float WebUnitsAcross => WebUnits;
        /// <summary>The web's turn (radians; the petals' rotY, -1.57..3.71 over the fx).</summary>
        public static float WebTwist(float ms) => Mathf.Lerp(-1.57f, 3.71f, Mathf.Clamp01(ms / LengthMs));

        public static float Ring(float ms) => Pulse(ms, RingStartMs, 90f, RingEndMs, 800f);
        /// <summary>The hole's radius in game units, into interstellar space: wide enough for the ship ('ship' its half span)
        /// as it flies through, at 'crossMs', and then running out past the camera.</summary>
        public static float RiftUnits(float ms, float crossMs, float ship)
        {
            float t = (ms - RingStartMs) / Mathf.Max(1f, crossMs - RingStartMs);
            if (t <= 0f) return 0f;
            float atCross = ship * 2.2f;
            return t <= 1f ? atCross * Mathf.SmoothStep(0f, 1f, Mathf.Sqrt(t)) : atCross * Mathf.Pow(t, 5f);
        }

        /// <summary>The flash: over in a blink, as in the original (a single key at 1733 ms), peaking at 1.</summary>
        public static float Flash(float ms, float peakMs)
        {
            float d = ms - peakMs;
            return d < 0f ? Mathf.Exp(d / 25f) : Mathf.Exp(-d / 140f);
        }

        /// <summary>A circle of 'radius' on a plane 'distance' ahead fills a view with this vertical half-angle tangent and aspect.</summary>
        public static bool Fills(float radius, float distance, float tanHalfFov, float aspect) =>
            distance <= 0f || radius >= distance * tanHalfFov * Mathf.Sqrt(1f + aspect * aspect);

        /// <summary>The charge's show (0..1 of the charge): lightning on the hull from a third of the way, the focus gathering
        /// in the last quarter.</summary>
        public static float ChargeArcs(float rate) => Mathf.SmoothStep(0f, 1f, Mathf.InverseLerp(0.33f, 1f, rate));
        public static float ChargeFocus(float rate) => Mathf.Pow(Mathf.InverseLerp(0.75f, 1f, rate), 2f);
    }
}
