// HangarInspect.cs
// Remake-only: the hangar's "Inspect ship". The camera leaves its drifting hangar spot (StationLevel.UpdateCamera), zooms in
// on the player's ship and orbits it like the flight's Action Freeze (UI.PhotoMode): yaw / pitch around the ship's centre
// (the bounds of its meshes), a distance between just outside the hull and a few times its size. The orbit starts where the
// hangar camera is, so the way in is a zoom and a turn toward the ship (BlendMs, the distance easing to FocusRadii x the
// radius); leaving blends back to the hangar camera the same way. The pitch keeps the camera above the hangar floor (the
// hull's lowest point, the pad). Input comes from UI.StationMenu (drag, wheel, pinch, keys, sticks, triggers). The Mod
// Builder also pans it (Pan, a middle-mouse drag): the point it orbits slides in the view's plane, at most MaxPanRadii of the
// ship's radius from its centre. Plain C#.

using UnityEngine;

namespace GoF2Remake.World
{
    public sealed class HangarInspect
    {
        const float BlendMs = 900f;             // hangar camera <-> orbit
        const float FocusRadii = 2.1f;          // the distance it zooms to on entering, in ship radii
        const float MinRadii = 1.15f, MaxRadii = 10f;   // Modified: was 4 (#181); the walls stop it now (Blocked)
        const float MaxPitch = 1.45f;           // rad (~83 deg): never straight over the top
        const float FloorClearance = 1.5f;      // m above the hull's lowest point
        const float ZoomEase = 0.008f;          // per ms
        const float MaxPanRadii = 2f;           // how far the orbit's point may be panned off the ship's centre
        const float PanPerPixel = 0.000783f;    // 2 tan(0.4) / 1080: one 1080p pixel at the hangar's 0.8 rad FOV, per metre of distance

        Vector3 centre, pan;
        float radius, floorY, yaw, pitch, distance, targetDistance, minDistance, maxDistance;

        /// <summary>Inspecting (entered and not left; the blend may still be under way).</summary>
        public bool Active { get; private set; }
        /// <summary>0 = the hangar camera .. 1 = the orbit camera (eased; it runs back to 0 after End).</summary>
        public float Blend { get; private set; }
        /// <summary>The camera is the orbit's, wholly or partly.</summary>
        public bool Shown => Active || Blend > 0f;
        public float Distance => distance;
        public float Radius => radius;
        public Vector3 Centre => centre;

        /// <summary>From the hangar camera at 'cameraPosition' around the ship of 'shipBounds' (world); 'minRadii': how close it
        /// may zoom, in ship radii (the Mod Builder goes closer than Inspect ship).</summary>
        public void Begin(Vector3 cameraPosition, Bounds shipBounds, float minRadii = MinRadii)
        {
            centre = shipBounds.center;
            radius = Mathf.Max(1f, shipBounds.extents.magnitude);
            // A pan stays through a re-begin (the Mod Builder rebuilds its ship), within the new radius; a fresh orbit starts
            // on the centre.
            pan = Active ? Vector3.ClampMagnitude(pan, radius * MaxPanRadii) : Vector3.zero;
            var focus = centre + pan;
            floorY = shipBounds.min.y;
            var d = cameraPosition - focus;
            distance = Mathf.Max(d.magnitude, radius * minRadii);
            minDistance = radius * minRadii;
            maxDistance = Mathf.Max(radius * MaxRadii, minDistance + 1f);
            yaw = Mathf.Atan2(d.x, d.z);
            pitch = Mathf.Asin(Mathf.Clamp(d.y / Mathf.Max(d.magnitude, 1e-3f), -1f, 1f));
            // Re-begun on a rebuilt ship (the Mod Builder): the zoom stays where it was; it jumped back to the first close-up.
            targetDistance = Mathf.Clamp(Active ? targetDistance : radius * FocusRadii, minDistance, maxDistance);
            shownDistance = 0f;   // Modified: the walls' limit found again for this orbit (the first Apply takes it at once)
            walled = false;
            Active = true;   // re-entered mid-blend, the blend goes on from where it is
        }

        public void End() => Active = false;

        /// <summary>Gone at once (the view left the hangar): no blend back.</summary>
        public void Reset() { Active = false; Blend = 0f; shownDistance = 0f; walled = false; ClearPhoto(); }

        /// <summary>The photo tools' free pose, roll and field of view gone (the blend back has ended).</summary>
        void ClearPhoto() { free = false; RollDeg = 0f; FovDeg = 0f; }

        /// <summary>Turns the orbit: + yaw takes the camera to its own left (the ship turns with a drag to the right), + pitch
        /// takes it up.</summary>
        public void Turn(float yawRad, float pitchRad)
        {
            yaw += yawRad;
            pitch += pitchRad;
        }

        /// <summary>Pans: the view follows a drag of 'pixels' (1080p pixels, y up) like grabbing the scene, the point it orbits
        /// kept within MaxPanRadii of the ship's radius from its centre.</summary>
        public void Pan(Vector2 pixels)
        {
            var dir = Direction;
            var right = Vector3.Cross(Vector3.up, -dir).normalized;
            var up = Vector3.Cross(-dir, right);
            pan -= (right * pixels.x + up * pixels.y) * PanPerPixel * distance;
            pan = Vector3.ClampMagnitude(pan, radius * MaxPanRadii);
        }

        /// <summary>Back to orbiting the ship's centre.</summary>
        public void ResetPan() => pan = Vector3.zero;

        /// <summary>Looks from 'worldDirection' (from the ship's centre toward the camera) at 'radii' x the ship's radius
        /// (0 = the distance kept), at once (the Mod Builder's MCP view tool); the pitch stays within the orbit's limits.</summary>
        public void SetView(Vector3 worldDirection, float radii)
        {
            var d = worldDirection.normalized;
            yaw = Mathf.Atan2(d.x, d.z);
            pitch = Mathf.Clamp(Mathf.Asin(Mathf.Clamp(d.y, -1f, 1f)), -MaxPitch, MaxPitch);
            if (radii > 0f) targetDistance = Mathf.Clamp(radius * radii, minDistance, maxDistance);
            distance = targetDistance;
        }

        // ---- remake: the photo tools (UI.PhotoTools: the free camera, the lens's roll and field of view) ----

        bool free;
        Vector3 freePos;
        Quaternion freeRot = Quaternion.identity;

        /// <summary>The camera flies free (World.FreeCamera) instead of orbiting: this pose replaces the orbit's.</summary>
        public void SetFreePose(Vector3 position, Quaternion rotation) { free = true; freePos = position; freeRot = rotation; }
        public void ClearFree() => free = false;
        public bool Free => free;
        /// <summary>The lens turned about the view (degrees, + counter-clockwise), the photo tools' Roll.</summary>
        public float RollDeg { get; set; }
        /// <summary>The photo tools' vertical field of view (degrees), 0 = the hangar camera's.</summary>
        public float FovDeg { get; set; }
        /// <summary>The floor the free camera stays above (m): the hull's lowest point + the orbit's clearance.</summary>
        public float FloorY => floorY + FloorClearance;
        /// <summary>The orbit camera's pose now (where the free camera starts).</summary>
        public Vector3 OrbitPosition => centre + pan + Direction * (shownDistance > 0f ? shownDistance : distance);   // Modified: where the walls let it be
        public Quaternion OrbitRotation => Quaternion.LookRotation(-Direction, Vector3.up);

        /// <summary>Modified: how far the camera may go from 'from' along 'dir' before the room's walls (at most 'max');
        /// null = no limit. Set by StationLevel.</summary>
        public System.Func<Vector3, Vector3, float, float> Blocked;
        /// <summary>Modified: the distance shown (the orbit's, or less where a wall is in the way); 0 = not found yet.</summary>
        float shownDistance;
        /// <summary>Modified: a wall held the camera in at the last Apply (it eases back out from there).</summary>
        bool walled;
        float tickMs;
        /// <summary>Modified: how fast the camera eases back out once a wall is out of the way (per ms; ~0.3 s).</summary>
        const float WallEase = 0.01f;
        /// <summary>The near plane's limit while the room's walls count (m), under StationLevel's 0.4 m camera sphere (its
        /// corners at the hangar's 0.8 rad field of view reach ~0.33 m).</summary>
        const float WallNear = 0.25f;

        /// <summary>Modified: the camera is where it is going: the blend in done, the zoom there and not easing out from a
        /// wall (the Mod Builder MCP's captures wait for it, ModBuilderViewAi.CameraSettled).</summary>
        public bool Settled => Active && Blend >= 1f && Mathf.Abs(distance - targetDistance) <= 0.002f * radius && (free || !walled);

        /// <summary>The orbit's pitch now (rad; the floor may have raised it).</summary>
        public float Pitch => pitch;

        Vector3 Direction => new Vector3(Mathf.Sin(yaw) * Mathf.Cos(pitch), Mathf.Sin(pitch), Mathf.Cos(yaw) * Mathf.Cos(pitch));

        /// <summary>Zooms: the distance it eases to x 'factor' (&lt; 1 = closer).</summary>
        public void Zoom(float factor) => targetDistance = Mathf.Clamp(targetDistance * factor, minDistance, maxDistance);

        /// <summary>Real time (the camera keeps moving when the game is slowed).</summary>
        public void Tick(float dtMs)
        {
            tickMs = dtMs;
            Blend = Mathf.MoveTowards(Blend, Active ? 1f : 0f, dtMs / BlendMs);
            if (!Active && Blend <= 0f) ClearPhoto();   // kept through the blend back, so the view doesn't jump
            distance = Mathf.Lerp(distance, targetDistance, 1f - Mathf.Exp(-ZoomEase * dtMs));
            // Above the floor: sin(pitch) * distance >= floor + clearance - centre.
            float minPitch = Mathf.Asin(Mathf.Clamp((floorY + FloorClearance - centre.y - pan.y) / Mathf.Max(distance, 1e-3f), -1f, 1f));
            pitch = Mathf.Clamp(pitch, Mathf.Max(minPitch, -MaxPitch), MaxPitch);
        }

        /// <summary>The camera between the hangar pose and the orbit's, by the eased blend. Returns the near plane (m) for the
        /// orbit (in front of the hull at the closest zoom), or 'hangarNear' when it isn't shown.</summary>
        public float Apply(Transform camera, Vector3 hangarPos, Quaternion hangarRot, float hangarNear)
        {
            if (!Shown) return hangarNear;
            var dir = Direction;
            // Modified: the orbit stays inside the room: the camera stops short of a wall between it and the hull (Blocked:
            // StationLevel's cast against the room's meshes), easing back out once the way is clear.
            // In at once (never through the wall); out at once where nothing held it (the zoom eases by itself: a per-frame
            // ease here lagged every zoom-out, by the frame rate); out from a wall eased by the time passed.
            float want = distance;
            if (!free && Blocked != null) want = Mathf.Min(distance, Mathf.Max(Blocked(centre + pan, dir, distance), radius * 1.05f));
            if (shownDistance <= 0f || want <= shownDistance || !walled) shownDistance = want;
            else shownDistance = Mathf.Lerp(shownDistance, want, 1f - Mathf.Exp(-WallEase * tickMs));
            walled = shownDistance < distance - 0.01f;
            var pos = free ? freePos : centre + pan + dir * shownDistance;
            var rot = (free ? freeRot : Quaternion.LookRotation(-dir, Vector3.up)) * Quaternion.AngleAxis(RollDeg, Vector3.forward);
            float b = Mathf.SmoothStep(0f, 1f, Blend);
            camera.SetPositionAndRotation(Vector3.Lerp(hangarPos, pos, b), Quaternion.Slerp(hangarRot, rot, b));
            // In front of the hull at the closest: the camera's distance from it (the free camera may go anywhere near it).
            float gap = (free ? Vector3.Distance(freePos, centre) : distance) - radius;
            float orbitNear = Mathf.Clamp(gap * 0.5f, free ? 0.05f : 0.1f, hangarNear);
            // Modified: never past the room's walls either: the camera stops 0.9 m from a wall (the free camera 0.4 m;
            // StationLevel's casts), and a near plane set by the hull's distance cut through the wall, showing what's behind
            // it (the user's report).
            if (Blocked != null) orbitNear = Mathf.Min(orbitNear, WallNear);
            return Mathf.Lerp(hangarNear, orbitNear, b);
        }
    }
}
