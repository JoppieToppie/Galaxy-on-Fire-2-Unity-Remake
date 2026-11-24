// OrbitScale.cs
// Remake, experimental (the user's request, 2026-11-15: "the stations are very small"): Options > Gameplay "Station scale"
// makes the orbit's station 1x..6x its size, and the orbit around it grows with it. All game coordinates stay the
// original's (OrbitLayout, the story levels, the traffic plans, the event graphs); what changes is where an orbit
// position lands in the scene: OrbitLayout.Place, a radial map about the station at the origin, applied where a
// station-anchored position becomes a Unity one (ToUnity here) and taken back where a Unity position becomes a game one
// to be compared with the original's coordinates (ToGame).
//   r <= R          r * F            inside the station's reach everything grows with the hull (the undock point, a
//                                    camera against the hull, a ship parked at a module stay where they were on it)
//   R < r < 2R      eased            the map's slope goes from F down to 1 (no kink for a group straddling R)
//   r >= 2R         r + 1.5 (F-1) R  further out the orbit is only pushed out by the station's growth: an encounter, a
//                                    convoy, a waypoint and the ships around it keep their own spacing and timing, and
//                                    the gap between the hull and the gate / the asteroids / a battle stays the original's
// F = the chosen scale, R = the station's reach (OrbitLayout.stationReach, game units, from its collision volumes). No
// station (an empty orbit or interstellar space), or 1x: the identity. Multiplayer uses the session mods’ shared minimums.
// Offsets from a ship or an object (a chase or cutscene camera beside it, a formation slot, a muzzle), speeds and
// ship-to-ship distances are never mapped. A distance from the station (the dock range, "near the station") maps with
// Radius. See docs/notes/space-scene.md "Station scale".

using GoF2Remake.Data;
using UnityEngine;

namespace GoF2Remake.World
{
    [Unity.Scripting.LifecycleManagement.NoAutoStaticsCleanup]
    public static class OrbitScale
    {
        /// <summary>The scales the option offers (Settings.StationScale snaps to them).</summary>
        public static readonly float[] Choices = { 1f, 1.5f, 2f, 2.5f, 3f, 4f, 5f, 6f };
        public const float Max = Modding.ModPlayerInfrastructure.MaxStructureScale;

        /// <summary>The orbit whose map applies now: the flight level's (SpaceLevel) or the menu backdrop's (MenuBackground);
        /// null: none (the identity).</summary>
        public static OrbitLayout Current { get; private set; }

        [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.SubsystemRegistration)]
        static void ResetStatics() => Current = null;

        /// <summary>The scale a new orbit takes: the option, 1 in anything belonging to a multiplayer session.</summary>
        public static float Chosen => NetSession.SessionGame || NetSession.Active ? 1f : Settings.StationScale;

        /// <summary>This orbit's map from now on (null: none).</summary>
        public static void Use(OrbitLayout layout) => Current = layout;

        /// <summary>The station's scale in the current orbit (1: none).</summary>
        public static float Factor => Current != null ? Current.StationScale : 1f;
        public static float JumpgateFactor => Current != null ? Current.JumpgateScale : 1f;

        /// <summary>The current orbit maps positions (a station, a scale above 1).</summary>
        public static bool Active => Current != null && Current.Scaled;

        /// <summary>Remake: a structure of the orbit (a station, a gate, an outpost, the Mining Plant, a story's platform or
        /// wreck of station size: not a ship, not a capital ship) grown like the station, about its own origin. Its position
        /// is the caller's (Place it); offsets on it (a docking point, a spot on its hull) scale with Factor.</summary>
        public static void Structure(Transform t) { if (t != null && Active) t.localScale *= Factor; }

        /// <summary>A structure's volumes grown with it (copies; offsets from its origin and sizes times Factor).</summary>
        public static System.Collections.Generic.List<Flight.CollisionVolume> StructureVolumes(System.Collections.Generic.List<Flight.CollisionVolume> volumes)
            => volumes == null || !Active ? volumes : Flight.CollisionVolume.Scaled(volumes, Factor);

        /// <summary>A station-anchored game position (the station at the origin) to where it lies in the scaled orbit (game units).</summary>
        public static Vector3 Place(Vector3 game) => Current != null ? Current.Place(game) : game;

        /// <summary>Place's inverse.</summary>
        public static Vector3 Unplace(Vector3 game) => Current != null ? Current.Unplace(game) : game;

        /// <summary>A distance from the station (game units, or metres: the map is linear in its unit) in the scaled orbit.</summary>
        public static float Radius(float r) => Current != null ? Current.PlaceRadius(r) : r;

        /// <summary>Radius' inverse.</summary>
        public static float Unradius(float r) => Current != null ? Current.UnplaceRadius(r) : r;

        /// <summary>Radius for metres: a distance from the station in Unity metres in the scaled orbit.</summary>
        public static float RadiusMeters(float meters) => Radius(meters / OrbitLayout.MetersPerUnit) * OrbitLayout.MetersPerUnit;

        /// <summary>A station-anchored game position to Unity, mapped (OrbitLayout.ToUnity(Place(game))).</summary>
        public static Vector3 ToUnity(Vector3 game) => OrbitLayout.ToUnity(Place(game));

        /// <summary>A Unity position to the original's game coordinates (Unplace of the plain conversion).</summary>
        public static Vector3 ToGame(Vector3 unity) => Unplace(new Vector3(unity.x, unity.y, -unity.z) / OrbitLayout.MetersPerUnit);

        /// <summary>A Unity position computed from the original's coordinates without the map (game * 0.05, z mirrored), mapped.</summary>
        public static Vector3 PlaceUnity(Vector3 unity) => Active ? OrbitLayout.ToUnity(Place(new Vector3(unity.x, unity.y, -unity.z) / OrbitLayout.MetersPerUnit)) : unity;

        /// <summary>PlaceUnity's inverse: a Unity position in the scaled orbit to where the original had it (still Unity).</summary>
        public static Vector3 UnplaceUnity(Vector3 unity) => Active ? OrbitLayout.ToUnity(ToGame(unity)) : unity;

        // ---- the radial map ------------------------------------------------------------------------------------------

        /// <summary>r -> r' for scale 'f' and reach 'reach' (see the header).</summary>
        public static float MapRadius(float r, float f, float reach)
        {
            if (f <= 1.0001f || reach <= 0f || r <= 0f) return r;
            if (r <= reach) return r * f;
            if (r >= 2f * reach) return r + 1.5f * (f - 1f) * reach;
            float x = r - reach;
            return f * reach + f * x - (f - 1f) * x * x / (2f * reach);
        }

        /// <summary>MapRadius' inverse.</summary>
        public static float UnmapRadius(float r, float f, float reach)
        {
            if (f <= 1.0001f || reach <= 0f || r <= 0f) return r;
            if (r <= f * reach) return r / f;
            float outer = 0.5f * reach + 1.5f * f * reach;   // MapRadius(2 reach)
            if (r >= outer) return r - 1.5f * (f - 1f) * reach;
            // (f - 1) / (2 reach) x^2 - f x + (r - f reach) = 0, the root in 0..reach
            float a = (f - 1f) / (2f * reach), c = r - f * reach;
            float disc = Mathf.Max(0f, f * f - 4f * a * c);
            return reach + (f - Mathf.Sqrt(disc)) / (2f * a);
        }

        public static Vector3 MapPoint(Vector3 p, float f, float reach)
        {
            float r = p.magnitude;
            if (r < 1e-4f || f <= 1.0001f || reach <= 0f) return p * (reach > 0f ? Mathf.Max(1f, f) : 1f);
            return p * (MapRadius(r, f, reach) / r);
        }

        public static Vector3 UnmapPoint(Vector3 p, float f, float reach)
        {
            float r = p.magnitude;
            if (r < 1e-4f || f <= 1.0001f || reach <= 0f) return reach > 0f && f > 1f ? p / f : p;
            return p * (UnmapRadius(r, f, reach) / r);
        }
    }
}
