// ModShipView.cs
// Remake mods: how a ship is seen, for new ships and overrides in ships.json (the GoF3 Ships mod writes them from Galaxy on
// Fire 3's own ship files):
//   "camera": { "distance", "height", "lookHeight", "lookAhead", "orbitDistance" }   chase-camera offsets and the free-look
//            orbit's default radius, in game units. The chase camera's original offsets are 1338 / 600 / 600 / -650.
//            orbitDistance defaults to 3800 and is clamped to the free-look zoom limits (1500..20000).
//   "cockpit": [x, y, z]            the cockpit view's seat (ChaseCamera.FirstPersonSeat; default: on the bow from the hull).
//   "smoke": [[x, y, z], ...]       where a fighter below a third of its hull trails smoke and fire (ShipSmoke; default:
//            the middle of the ship), at most 4.
//   "explosionScale": n             the explosion's size when the ship dies (NPC fighters, the player, other players'
//            ships; the fighters' 1, 0.25..20). Freighters and capital ships keep their own.
// Points are game units in the mounts' axes (position_engine: +x the pilot's left, +y up, +z the nose).

using System;
using System.Collections.Generic;
using GoF2Remake.Data;
using Newtonsoft.Json.Linq;
using UnityEngine;

namespace GoF2Remake.Modding
{
    // (its statics are constant tables: nothing to reset between plays)
    [Unity.Scripting.LifecycleManagement.NoAutoStaticsCleanup]
    public static class ModShipView
    {
        public const int MaxSmokePoints = 4;
        public const float MinExplosion = 0.25f, MaxExplosion = 20f;
        public const float DefaultOrbitDistance = 3800f, MinOrbitDistance = 1500f, MaxOrbitDistance = 20000f;

        /// <summary>The original's chase camera (TargetFollowCamera): distance, height, look height, look ahead.</summary>
        public static readonly float[] DefaultCamera = { 1338f, 600f, 600f, -650f };

        static readonly string[] CameraFields = { "distance", "height", "lookHeight", "lookAhead" };

        /// <summary>ModContent.ParseShips: the fields are readable (errors with file and line).</summary>
        public static void CheckShip(JObject o, string where, ModInfo mod, string file)
        {
            if (ModJson.Get(o, "camera") is JToken c)
            {
                if (!(c is JObject co)) throw new ModJsonException($"{where}: \"camera\" must be an object {{ \"distance\": 2400, \"height\": 900 }}");
                foreach (var prop in co.Properties())
                {
                    if (Array.FindIndex(CameraFields, f => string.Equals(f, prop.Name, StringComparison.OrdinalIgnoreCase)) < 0
                        && !string.Equals(prop.Name, "orbitDistance", StringComparison.OrdinalIgnoreCase))
                    {
                        string warning = $"{ModJson.Where(prop, file)}: unknown camera field \"{prop.Name}\" (ignored)";
                        if (!mod.Warnings.Contains(warning)) mod.Warnings.Add(warning);
                        continue;
                    }
                    if (!IsNumber(prop.Value)) throw new ModJsonException($"{ModJson.Where(prop, file)}: \"{prop.Name}\" must be a number");
                }
                if (ModJson.Get(co, "distance") is JToken d && IsNumber(d) && (float)d <= 0f)
                    throw new ModJsonException($"{ModJson.Where(d, file)}: \"distance\" must be above 0");
                if (ModJson.Get(co, "orbitDistance") is JToken od && IsNumber(od))
                {
                    if ((float)od <= 0f) throw new ModJsonException($"{ModJson.Where(od, file)}: \"orbitDistance\" must be above 0");
                    if ((float)od < MinOrbitDistance || (float)od > MaxOrbitDistance)
                        mod.Warnings.Add($"{ModJson.Where(od, file)}: \"orbitDistance\" {(float)od} is outside {MinOrbitDistance}..{MaxOrbitDistance} (clamped)");
                }
            }
            if (ModJson.Get(o, "cockpit") is JToken k && !TryPoint(k, out _))
                throw new ModJsonException($"{ModJson.Where(k, file)}: \"cockpit\" must be a point [x, y, z]");
            if (ModJson.Get(o, "smoke") is JToken s)
            {
                if (!(s is JArray list)) throw new ModJsonException($"{ModJson.Where(s, file)}: \"smoke\" must be a list of points [[x, y, z], ...]");
                foreach (var p in list)
                    if (!TryPoint(p, out _)) throw new ModJsonException($"{ModJson.Where(p, file)}: each smoke point must be [x, y, z]");
                if (list.Count > MaxSmokePoints) mod.Warnings.Add($"{ModJson.Where(s, file)}: \"smoke\" has {list.Count} points, only the first {MaxSmokePoints} are used");
            }
            if (ModJson.Get(o, "explosionScale") is JToken e)
            {
                if (!IsNumber(e)) throw new ModJsonException($"{ModJson.Where(e, file)}: \"explosionScale\" must be a number");
                float v = (float)e;
                if (v < MinExplosion || v > MaxExplosion)
                    mod.Warnings.Add($"{ModJson.Where(e, file)}: \"explosionScale\" {v} is outside {MinExplosion}..{MaxExplosion} (clamped)");
            }
        }

        /// <summary>ModContent.ApplyShips: the fields onto the ship (a new ship or an override; an override without them keeps
        /// what an earlier mod gave).</summary>
        public static void ApplyShip(ShipData s, JObject o)
        {
            if (ModJson.Get(o, "camera") is JObject c)
            {
                var cam = (float[])(s.chaseCamera ?? DefaultCamera).Clone();
                for (int i = 0; i < CameraFields.Length; i++)
                    if (ModJson.Get(c, CameraFields[i]) is JToken v && IsNumber(v)) cam[i] = (float)v;
                if (cam[0] <= 0f) cam[0] = DefaultCamera[0];
                s.chaseCamera = cam;
                if (ModJson.Get(c, "orbitDistance") is JToken od && IsNumber(od))
                    s.orbitCameraDistance = Mathf.Clamp((float)od, MinOrbitDistance, MaxOrbitDistance);
            }
            if (ModJson.Get(o, "cockpit") is JToken k && TryPoint(k, out var seat)) s.cockpit = seat;
            if (ModJson.Get(o, "smoke") is JArray list)
            {
                var points = new List<Vector3>();
                foreach (var p in list)
                    if (points.Count < MaxSmokePoints && TryPoint(p, out var v)) points.Add(v);
                s.smokePoints = points.Count > 0 ? points : null;
            }
            if (ModJson.Get(o, "explosionScale") is JToken e && IsNumber(e)) s.explosionScale = Mathf.Clamp((float)e, MinExplosion, MaxExplosion);
        }

        /// <summary>The free-look orbit's default radius in game units; absent = the original's 3800.</summary>
        public static float OrbitDistance(ShipData s) => s?.orbitCameraDistance ?? DefaultOrbitDistance;

        /// <summary>The chase camera's offsets in the ship's frame (metres; game units x 'metersPerUnit'): the ship's own
        /// "camera", else the original's.</summary>
        public static void CameraOffsets(ShipData s, float metersPerUnit, out Vector3 offset, out Vector3 lookOffset)
        {
            var c = s?.chaseCamera ?? DefaultCamera;
            offset = new Vector3(0f, c[1], -c[0]) * metersPerUnit;
            lookOffset = new Vector3(0f, c[2], c[3]) * metersPerUnit;
        }

        /// <summary>A point in game units (position_engine's axes) in the ship model's frame in metres, like a mount.</summary>
        public static Vector3 ToLocal(Vector3 game, float metersPerUnit) => new Vector3(-game.x, game.y, game.z) * metersPerUnit;

        static bool IsNumber(JToken t) => t != null && (t.Type == JTokenType.Integer || t.Type == JTokenType.Float);

        static bool TryPoint(JToken t, out Vector3 p)
        {
            p = default;
            if (!(t is JArray a) || a.Count != 3 || !IsNumber(a[0]) || !IsNumber(a[1]) || !IsNumber(a[2])) return false;
            p = new Vector3((float)a[0], (float)a[1], (float)a[2]);
            return true;
        }
    }
}
