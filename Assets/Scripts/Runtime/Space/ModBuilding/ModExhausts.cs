// ModExhausts.cs
// Remake mods: exhaust styles, engine effects a mod offers for every ship (the hangar's Customize screen: the game's own
// ships, mod ships and customizable ones alike). A mod's exhausts.json lists them:
//   [ { "id": "nivelian", "name": "Nivelian drive" | { "en": ..., "de": ... },
//       "engineFx": { ... },          Galaxy on Fire 3's engine effect as GoF3 draws it (ships.json's "engineFx", EngineFx:
//                                     its engine scene's "meshes" and its Fork particles), or
//       "model": "exhausts/thruster.glb", "materials": [ { "material": "EngineExhaust", "flame": true, ... } ],
//                                     a flame model put at each exhaust (EVERSPACE 2's thrusters: flame cards GoF2/ModEngineFlame
//                                     stretches with the thrust, EngineFlame); its origin at the nozzle's exit, the flame
//                                     blowing along -Z (the ship's back), "yaw" degrees about y when the model faces another way
//       "nozzle": 87,                 the nozzle radius the effect is made for: game units for "engineFx" (default its
//                                     engineFx's "nozzle", else 87), model units for "model" (default 1); each
//                                     exhaust is scaled by its own nozzle's size against it (NozzleRadius: a mod mount's
//                                     glowSize, a mod ship's engineGlowRadius, a game ship's exhaust anchor scale)
//       "palette": [ { "id", "name", "color": "#RRGGBB" }, ... ],   the colours offered (default the built-in bright ones)
//       "group": "thruster", "groupName": "Thruster", "scale": 3.6
//                                     flame models of one mod with the same group are one choice (named by groupName,
//                                     else its first's name): each nozzle wears the member whose nozzle (x "scale",
//                                     Unity metres per model unit as the model's own ships show it) is nearest its size
//       "mainName" / "secondName": the colour rows' names (default "Exhaust colour" / "Second colour": the flames and their
//                                 boost cards; an "engineFx"'s parts, then its helix and particles, each pixel's hue
//                                 replaced and its white kept)
//   } ]
// A build carries the choice ("exhaust=<mod_id:id>", "exhaustcolor=<palette id or #RRGGBB>", "exhaustcolor2=...", and
// "exhaustsize=1.2", the player's resizing of it, x the fitted size; ModShipKits Build), so the save, other players' views (NetPlayer's build text) and the hangar guests show it. Apply puts it on a ship
// model's instance: under the engine glow part (so it goes with the engines, NetPlayer's / the hangar's engine state, the
// cloak) a holder aligned with the ship's root, the effect inside it (EngineFx, or the flame model per exhaust with an
// EngineFlame of its own); the model's own exhaust is hidden: the glow part's renderers (forceRenderingOff: the flight's
// engine switch only toggles 'enabled'), a built-in EngineFx switched off, the model's own flames kept dark (EngineFlame
// suppressed), no particle plume (ShipExhaust finds the holder's effect). The manoeuvring jets stay.
// Each exhaust takes the shape of the nozzle it sits on (NozzleShape: half width, half height and centre; ShapeOf): a mod
// ship's nozzle meshes (materials named "nozzle", measured on the instance: EVERSPACE 2's rings and grilles), else its mount's
// glowSize (its glowShape's outline), else the game's ships' measured glow (GoF2 > Build > Nozzle Shapes,
// nozzle_shapes.json), else round (NozzleRadius); a mount's outline only when plausible against its glowSize (Plausible). On a ship with GoF3's engine effect of
// its own the shapes are scaled so that their typical size is the nozzle that effect is drawn for (FitToOwnEffect: the GoF3
// builder measures only the hot core of each nozzle's glow). A flame model is scaled per axis to it, an
// engineFx's parts are spread to it (all but its sprites); both centred on it.
// Offered (For / Offered): every style on every ship but a flame style on its own mod's ships, whose own exhaust parts it
// was cut from (the ES2 thrusters on the ES2 ships).
// A flame style's boost cards are its own mod's ships' boost jets (EVERSPACE 2's): on any other ship (the game's, another
// mod's: there are no boost jets to light) they stay hidden and a boost turns each nozzle's main flame to the boost colour
// instead (EngineFlame.boostOverride, GoF2/ModEngineFlame's _BoostOverride). A flame model loads with
// the mods' ship models (ModShips) as a part of the style's own kit.

using System;
using System.Collections.Generic;
using System.Linq;
using GoF2Remake.Data;
using GoF2Remake.Flight;
using GoF2Remake.Visuals;
using Newtonsoft.Json.Linq;
using UnityEngine;

namespace GoF2Remake.Modding
{
    [Unity.Scripting.LifecycleManagement.NoAutoStaticsCleanup]
    public static class ModExhausts
    {
        public const string File = "exhausts.json";
        /// <summary>The holder of a customized exhaust under a ship's engine glow part, and its nozzles' name prefix.</summary>
        public const string HolderName = "exhaust_style", NozzlePrefix = "exhaust_nozzle_";
        const float M = 0.05f;

        static readonly HashSet<string> Fields = new HashSet<string>(StringComparer.OrdinalIgnoreCase)
            { "id", "name", "engineFx", "model", "materials", "nozzle", "yaw", "palette", "mainName", "secondName", "group", "groupName", "scale" };

        public sealed class Style
        {
            public ModInfo mod;
            public string localId, model;
            public Dictionary<string, string> name, mainName, secondName;
            public CustomEngineFx fx;
            public List<CustomShipMaterial> materials = new List<CustomShipMaterial>();
            public float nozzle, yaw;
            /// <summary>Unity metres per model unit as the model's own ships show it (EVERSPACE 2's parts: 3.6): a group's
            /// members are compared with a nozzle's size by their nozzle x this.</summary>
            public float scale = 1f;
            public List<ModShipKits.ColorChoice> palette;
            /// <summary>A flame style's model loads as a part of this kit (ModShips); null for an "engineFx" style.</summary>
            public ModShipKits.Kit kit;
            /// <summary>A flame style's recoloured flame materials, by source and colours.</summary>
            public readonly Dictionary<(Material, Color32, Color32), Material> flameCopies = new Dictionary<(Material, Color32, Color32), Material>();
            /// <summary>Flame styles of one mod with the same "group" are one choice (the first of them, Lead): each nozzle
            /// wears the member made for the nearest nozzle size (EVERSPACE 2's three thrusters as one, ForNozzle).</summary>
            public string group;
            public Dictionary<string, string> groupName;
            public Style lead;
            public readonly List<Style> members = new List<Style>();
            public Style Lead => lead ?? this;
            public string Key => mod.Id + ":" + localId;
            public string Name => ModManifest.Pick(groupName) ?? ModManifest.Pick(name) ?? localId;

            /// <summary>The member for a nozzle of 'radius' (Unity metres): the one whose nozzle (x its scale) is nearest on a
            /// log scale (the least stretched); the style itself when it has no group.</summary>
            public Style ForNozzle(float radius)
            {
                if (members.Count < 2 || radius <= 0f) return this;
                Style best = this; float bestD = float.MaxValue;
                foreach (var m in members)
                {
                    float d = Mathf.Abs(Mathf.Log(radius / Mathf.Max(1e-4f, m.nozzle * m.scale)));
                    if (d < bestD) { bestD = d; best = m; }
                }
                return best;
            }
            public bool IsFlame => !string.IsNullOrEmpty(model);
            public List<ModShipKits.ColorChoice> Colors => palette != null && palette.Count > 0 ? palette : ModShipKits.WeaponPalette;
            public string MainName => ModManifest.Pick(mainName) ?? Localization.Extra("kitExhaustColor", "Exhaust colour");
            public string SecondName => ModManifest.Pick(secondName) ?? Localization.Extra("kitExhaustColor2", "Second colour");
        }

        static readonly List<Style> styles = new List<Style>();
        static int parsedRevision = -1;

        [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.SubsystemRegistration)]
        static void ResetStatics() { styles.Clear(); parsedRevision = -1; }

        /// <summary>The exhaust styles of the mods that are on, in the load order.</summary>
        public static List<Style> All()
        {
            if (parsedRevision == ModManager.Revision) return styles;
            parsedRevision = ModManager.Revision;
            styles.Clear();
            foreach (var mod in ModManager.Active)
            {
                try { Read(mod); }
                catch (ModJsonException e) { Warn(mod, e.Message); }
            }
            // The groups: the first flame style of a mod's group leads it, the others are its members (not offered alone).
            var leads = new Dictionary<string, Style>(StringComparer.OrdinalIgnoreCase);
            foreach (var s in styles)
            {
                if (string.IsNullOrEmpty(s.group)) continue;
                if (!s.IsFlame) { Warn(s.mod, $"{File}: \"{s.localId}\": \"group\" joins flame models only (an engineFx style stays alone)"); continue; }
                string key = s.mod.Id + ":" + s.group;
                if (!leads.TryGetValue(key, out var lead)) leads[key] = lead = s;
                else { s.lead = lead; if (lead.groupName == null) lead.groupName = s.groupName; }
                lead.members.Add(s);
            }
            return styles;
        }

        public static bool Any => All().Count > 0;

        /// <summary>The styles offered on 'ship' (the Customize screen): every one but a flame style on its own mod's ships. A
        /// flame style is cut from the mod's own exhaust parts (EVERSPACE 2's rears), so on those ships it would only repeat
        /// what their parts already choose; any other ship may wear it.</summary>
        public static List<Style> For(int ship) => All().FindAll(s => s.lead == null && Offered(s, ship));

        public static bool Offered(Style style, int ship) => style != null && !(style.IsFlame && OwnShip(ship, style));

        /// <summary>A style by key when 'ship' is offered it (null: none, its mod off, or not for this ship); a group's member
        /// is its group (an older build's key of one of EVERSPACE 2's thrusters).</summary>
        public static Style Find(string key, int ship) => Find(key)?.Lead is Style s && Offered(s, ship) ? s : null;

        /// <summary>A style by "mod_id:id" (null: none, "none", or its mod is off).</summary>
        public static Style Find(string key) =>
            string.IsNullOrEmpty(key) || key == ModShipKits.Build.NoSkin ? null : All().Find(s => string.Equals(s.Key, key, StringComparison.OrdinalIgnoreCase));

        static void Read(ModInfo mod)
        {
            if (!(ModJson.Read(mod.Source, File) is JToken token)) return;
            if (!(token is JArray list)) throw new ModJsonException($"{File}: must be a list [ {{ ... }}, ... ]");
            var ids = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
            foreach (var t in list)
            {
                string where = ModJson.Where(t, File);
                if (!(t is JObject o)) throw new ModJsonException($"{where}: each exhaust must be an object {{ ... }}");
                foreach (var prop in o.Properties())
                    if (!Fields.Contains(prop.Name)) Warn(mod, $"{ModJson.Where(prop, File)}: unknown field \"{prop.Name}\" (ignored)");
                var s = new Style
                {
                    mod = mod, localId = ModJson.Str(o, "id"), name = ModJson.Text(o, "name"), model = ModJson.Str(o, "model"),
                    mainName = ModJson.Text(o, "mainName"), secondName = ModJson.Text(o, "secondName"), yaw = ModJson.Float(o, "yaw", 0f, File),
                    group = ModJson.Str(o, "group"), groupName = ModJson.Text(o, "groupName"), scale = ModJson.Float(o, "scale", 1f, File),
                };
                if (string.IsNullOrEmpty(s.localId) || !ModManifest.ValidId(s.localId))
                    throw new ModJsonException($"{where}: \"id\" is missing or uses more than a-z, 0-9, _ and -");
                if (!ids.Add(s.localId)) throw new ModJsonException($"{where}: the id \"{s.localId}\" is used twice");
                if (ModJson.Get(o, "engineFx") is JToken ft)
                {
                    ModContent.CheckEngineFx(ft, where, mod);   // the old imitation's fields: a warning
                    try { s.fx = JsonUtility.FromJson<CustomEngineFx>(ft.ToString()); }
                    catch (Exception e) { throw new ModJsonException($"{where}: \"engineFx\": {e.Message}"); }
                    if (s.fx == null || !s.fx.Any) throw new ModJsonException($"{where}: \"engineFx\" names no engine (\"meshes\" or \"trailParticles\")");
                    if (!string.IsNullOrEmpty(s.fx.meshes) && !mod.Source.Exists(s.fx.meshes)) throw new ModJsonException($"{where}: the engine \"{s.fx.meshes}\" isn't in the mod");
                    foreach (var em in new[] { s.fx.trailParticles, s.fx.boostParticles })
                        if (em?.Any == true && !mod.Source.Exists(em.texture)) throw new ModJsonException($"{where}: the picture \"{em.texture}\" isn't in the mod");
                }
                if (s.IsFlame)
                {
                    if (s.fx != null) throw new ModJsonException($"{where}: give \"engineFx\" or \"model\", not both");
                    if (!mod.Source.Exists(s.model)) throw new ModJsonException($"{where}: the model \"{s.model}\" isn't in the mod");
                    if (ModJson.Get(o, "materials") is JToken mt)
                    {
                        try { s.materials = JsonUtility.FromJson<MaterialList>("{\"list\":" + mt + "}").list ?? new List<CustomShipMaterial>(); }
                        catch (Exception e) { throw new ModJsonException($"{where}: \"materials\": {e.Message}"); }
                    }
                    if (!s.materials.Exists(m => m != null && m.flame)) Warn(mod, $"{where}: no \"materials\" entry with \"flame\": true (the model shows, but nothing stretches with the thrust)");
                    // Its model loads as the only part of a kit of its own (ModShips.LoadAll / EnsureExhaust).
                    s.kit = new ModShipKits.Kit { mod = mod, localId = "~exhaust_" + s.localId, materials = s.materials };
                }
                else if (s.fx == null) throw new ModJsonException($"{where}: give \"engineFx\" (Galaxy on Fire 3's engine effect) or \"model\" (a flame model)");
                s.nozzle = ModJson.Float(o, "nozzle", s.IsFlame ? 1f : Mathf.Max(1f, s.fx.NozzleRadius), File);
                if (s.nozzle <= 0f) throw new ModJsonException($"{where}: \"nozzle\" must be above 0");
                if (ModJson.Get(o, "palette") is JToken pt)
                {
                    if (!(pt is JArray pa)) throw new ModJsonException($"{where}: \"palette\" must be a list of colours");
                    s.palette = ModShipKits.ReadPalette(pa, File);
                }
                styles.Add(s);
            }
        }

        [Serializable] sealed class MaterialList { public List<CustomShipMaterial> list; }

        static void Warn(ModInfo mod, string message)
        {
            if (!mod.Warnings.Contains(message)) mod.Warnings.Add(message);
            Debug.LogWarning($"Mods: {mod.Id}: {message}");
        }

        // ---- nozzle shapes ----------------------------------------------------------------------------------------------

        /// <summary>A nozzle's outline is kept as its radius every 360 / ProfileSteps degrees around its centre.</summary>
        public const int ProfileSteps = 32;

        /// <summary>A nozzle's shape: its outline (the convex hull of its geometry in the mount's plane) as 'profile', the
        /// radius (game units) at ProfileSteps angles from the plane's +x (Unity: the model's right) toward +y; its extents
        /// (half width / height, game units) and its centre's offset from the mount (Unity metres in the mount's plane);
        /// 'mount' the mount's position (Unity metres in the model) it was measured at (the data file).</summary>
        [Serializable] public sealed class NozzleShape
        {
            public float[] mount;
            public float halfWidth, halfHeight, centreX, centreY;
            public float[] profile;

            /// <summary>Its mean radius (game units).</summary>
            public float Mean => profile != null && profile.Length > 0 ? profile.Average() : Mathf.Sqrt(Mathf.Max(0f, halfWidth * halfHeight));

            /// <summary>Its outline against its mean radius at 'angle' (radians in the mount's plane): 1 for a round one.</summary>
            public float Factor(float angle) => FactorOf(Normalized(), angle);

            /// <summary>The profile divided by its mean (null: round).</summary>
            public float[] Normalized()
            {
                if (profile == null || profile.Length < 3) return null;
                float mean = Mean;
                return mean > 0f ? profile.Select(r => r / mean).ToArray() : null;
            }

            public NozzleShape Scaled(float k) => new NozzleShape
            {
                mount = mount, halfWidth = halfWidth * k, halfHeight = halfHeight * k, centreX = centreX, centreY = centreY,
                profile = profile?.Select(r => r * k).ToArray(),
            };
        }

        /// <summary>A normalized profile at 'angle' (radians from +x toward +y), between its samples (1 without one).</summary>
        public static float FactorOf(float[] normalized, float angle)
        {
            if (normalized == null || normalized.Length < 3) return 1f;
            int n = normalized.Length;
            float x = Mathf.Repeat(angle / (2f * Mathf.PI) * n, n);
            int i = (int)x;
            return Mathf.Lerp(normalized[i % n], normalized[(i + 1) % n], x - i);
        }

        [Serializable] public sealed class ShipShapes { public int ship; public List<NozzleShape> nozzles = new List<NozzleShape>(); }
        [Serializable] public sealed class ShapeFile { public List<ShipShapes> ships = new List<ShipShapes>(); }

        /// <summary>The game's glow meshes sit a little inside the nozzles (their median size is 0.91 of the round estimate
        /// the styles were fitted with).</summary>
        const float GlowToNozzle = 1.1f;
        const float MinHalf = 5f;   // game units: a smaller measurement is a stray vertex or two

        static ShapeFile gameShapes;

        static ShapeFile GameShapes()
        {
            if (gameShapes != null) return gameShapes;
            var t = Resources.Load<TextAsset>("GoF2Data/nozzle_shapes");
            try { gameShapes = t != null ? JsonUtility.FromJson<ShapeFile>(t.text) : null; } catch (Exception) { gameShapes = null; }
            return gameShapes ??= new ShapeFile();
        }

        /// <summary>The shape of points in a mount's plane (Unity metres from the mount): their convex hull, its area centroid
        /// as the centre, its radius from there at each profile angle; null for too few points or a sliver ('strict': smaller
        /// than MinHalf, or its centre far off).</summary>
        public static NozzleShape FromPoints(List<Vector2> points, bool strict = true)
        {
            var hull = ConvexHull(points);
            if (hull.Count < 3) return null;
            // The area centroid (a vertex-heavy side doesn't pull it).
            float area = 0f; var c = Vector2.zero;
            for (int i = 0; i < hull.Count; i++)
            {
                var p = hull[i]; var q = hull[(i + 1) % hull.Count];
                float cross = p.x * q.y - q.x * p.y;
                area += cross; c += (p + q) * cross;
            }
            if (Mathf.Abs(area) < 1e-8f) return null;
            c /= 3f * area;
            Vector2 min = hull[0], max = hull[0];
            foreach (var p in hull) { min = Vector2.Min(min, p); max = Vector2.Max(max, p); }
            var half = (max - min) * (0.5f / M);
            if (strict && (Mathf.Min(half.x, half.y) < MinHalf || c.magnitude > 1.5f * Mathf.Max(half.x, half.y) * M)) return null;
            var profile = new float[ProfileSteps];
            for (int k = 0; k < ProfileSteps; k++)
            {
                float a = 2f * Mathf.PI * k / ProfileSteps;
                var d = new Vector2(Mathf.Cos(a), Mathf.Sin(a));
                float best = float.MaxValue;
                for (int i = 0; i < hull.Count; i++)
                {
                    // c + t d = p + u (q - p): the ray's exit through this edge
                    var p = hull[i] - c; var e = hull[(i + 1) % hull.Count] - hull[i];
                    float den = d.x * e.y - d.y * e.x;
                    if (Mathf.Abs(den) < 1e-9f) continue;
                    float t = (p.x * e.y - p.y * e.x) / den, u = (p.x * d.y - p.y * d.x) / den;
                    if (t > 0f && u >= -1e-4f && u <= 1f + 1e-4f) best = Mathf.Min(best, t);
                }
                profile[k] = (best < float.MaxValue ? best : Mathf.Max(half.x, half.y) * M) / M;
            }
            float floor = MinHalf * 0.5f;
            for (int k = 0; k < ProfileSteps; k++) profile[k] = Mathf.Max(profile[k], floor);
            return new NozzleShape { halfWidth = half.x, halfHeight = half.y, centreX = c.x, centreY = c.y, profile = profile };
        }

        /// <summary>Andrew's monotone chain, counter-clockwise.</summary>
        static List<Vector2> ConvexHull(List<Vector2> points)
        {
            var pts = points.Distinct().OrderBy(p => p.x).ThenBy(p => p.y).ToList();
            if (pts.Count < 3) return pts;
            float Cross(Vector2 o, Vector2 a, Vector2 b) => (a.x - o.x) * (b.y - o.y) - (a.y - o.y) * (b.x - o.x);
            var hull = new List<Vector2>();
            foreach (var p in pts) { while (hull.Count >= 2 && Cross(hull[hull.Count - 2], hull[hull.Count - 1], p) <= 0f) hull.RemoveAt(hull.Count - 1); hull.Add(p); }
            int lower = hull.Count + 1;
            for (int i = pts.Count - 2; i >= 0; i--)
            {
                var p = pts[i];
                while (hull.Count >= lower && Cross(hull[hull.Count - 2], hull[hull.Count - 1], p) <= 0f) hull.RemoveAt(hull.Count - 1);
                hull.Add(p);
            }
            hull.RemoveAt(hull.Count - 1);
            return hull;
        }

        /// <summary>A mount's own "outline" (game units: [x0, y0, x1, y1, ...] around it, position_engine's axes) as its nozzle's
        /// shape, when it is plausible for the mount (Plausible); null otherwise.</summary>
        public static NozzleShape OutlineOf(WeaponMount m)
        {
            if (m?.outline == null || m.outline.Length < 6) return null;
            var pts = new List<Vector2>();
            for (int k = 0; k + 1 < m.outline.Length; k += 2) pts.Add(new Vector2(-m.outline[k] * M, m.outline[k + 1] * M));   // game x -> Unity's -x
            var o = FromPoints(pts, false);
            return Plausible(o, m) ? o : null;
        }

        /// <summary>An oval nozzle (half width / height, game units).</summary>
        static NozzleShape Ellipse(float hw, float hh) => Glow(hw, hh, null);

        /// <summary>A mount's glow outline (EngineGlowShape, none = an ellipse) in a half width x half height box.</summary>
        static NozzleShape Glow(float hw, float hh, string shape)
        {
            var profile = new float[ProfileSteps];
            for (int k = 0; k < ProfileSteps; k++) profile[k] = EngineGlowShape.Radius(shape, hw, hh, 2f * Mathf.PI * k / ProfileSteps);
            return new NozzleShape { halfWidth = hw, halfHeight = hh, profile = profile };
        }

        /// <summary>Points (the model's frame, Unity metres) shared out to their nearest mount (within 'maxDist' metres), each
        /// mount's in its own plane (its turn undone): their shape (FromPoints), null where too few or too far off.</summary>
        public static NozzleShape[] Measure(IList<Vector3> points, IList<Vector3> mounts, IList<Quaternion> turns, float maxDist)
        {
            int n = mounts.Count;
            var sets = new List<Vector2>[n];
            for (int i = 0; i < n; i++) sets[i] = new List<Vector2>();
            float limit = maxDist * maxDist;
            foreach (var p in points)
            {
                int best = -1; float bd = limit;
                for (int i = 0; i < n; i++) { float d = (p - mounts[i]).sqrMagnitude; if (d < bd) { bd = d; best = i; } }
                if (best >= 0) sets[best].Add(Quaternion.Inverse(turns[best]) * (p - mounts[best]));
            }
            var result = new NozzleShape[n];
            for (int i = 0; i < n; i++) if (sets[i].Count >= 3) result[i] = FromPoints(sets[i]);
            return result;
        }

        /// <summary>A mod ship's nozzle geometry on its instance: the readable meshes whose materials are named "nozzle" (not a
        /// customized exhaust's, not the jets), in the model's frame.</summary>
        static List<Vector3> NozzlePoints(GameObject model)
        {
            var points = new List<Vector3>();
            foreach (var mf in model.GetComponentsInChildren<MeshFilter>(true))
            {
                var mesh = mf.sharedMesh;
                if (mesh == null || !mesh.isReadable || InHolder(mf.transform) || IsJet(mf.transform)) continue;
                var r = mf.GetComponent<Renderer>();
                if (r == null || !Array.Exists(r.sharedMaterials, m => m != null && m.name.IndexOf("nozzle", StringComparison.OrdinalIgnoreCase) >= 0)) continue;
                foreach (var v in mesh.vertices) points.Add(model.transform.InverseTransformPoint(mf.transform.TransformPoint(v)));
            }
            return points;
        }

        /// <summary>The shape of each exhaust in 'exhausts': the mount's "outline" (game units: [x0, y0, x1, y1, ...] around
        /// it, position_engine's axes), a mod ship's nozzle meshes measured, the mount's glowSize as an oval, the game ship's
        /// measured glow (nozzle_shapes.json), else round (NozzleRadius).</summary>
        static NozzleShape[] ShapesOf(int ship, GameObject model, List<WeaponMount> exhausts)
        {
            var at = exhausts.ConvertAll(WeaponSystem.MountToLocal);
            bool modShip = CustomShips.Get(ship) != null || ModContent.ShipKey(ship) != null;
            NozzleShape[] meshes = null;
            if (modShip)
            {
                var points = NozzlePoints(model);
                if (points.Count > 0)
                    meshes = Measure(points, at, exhausts.ConvertAll(ModHardpoints.MountTurn),
                                     Mathf.Max(3f, 3f * exhausts.Max(m => NozzleRadius(ship, m)) * M));
            }
            var game = modShip ? null : GameShapes().ships.Find(s => s.ship == ship);
            var shapes = new NozzleShape[exhausts.Count];
            for (int i = 0; i < exhausts.Count; i++)
            {
                var m = exhausts[i];
                if (OutlineOf(m) is NozzleShape o) { shapes[i] = o; continue; }
                if (meshes?[i] != null) { shapes[i] = meshes[i]; continue; }
                if (m.glowSize != null && m.glowSize.Length >= 2 && m.glowSize[0] > 0f && m.glowSize[1] > 0f)
                {
                    shapes[i] = Glow(m.glowSize[0], m.glowSize[1], m.glowShape);
                    continue;
                }
                var g = game?.nozzles.Find(x => x.halfWidth > 0f && x.mount != null && x.mount.Length >= 3
                                                && (new Vector3(x.mount[0], x.mount[1], x.mount[2]) - at[i]).sqrMagnitude < 0.09f);
                if (g != null) { shapes[i] = g.Scaled(GlowToNozzle); continue; }
                float r = NozzleRadius(ship, m);
                shapes[i] = Ellipse(r, r);
            }
            return FitToOwnEffect(ship, exhausts, shapes);
        }

        /// <summary>A ship with Galaxy on Fire 3's engine effect of its own ("engineFx") that names its "nozzle": its nozzles as
        /// large as the nozzle that effect is drawn for (x each mount's fxScale) on the median, each keeping its shape and
        /// its size against the others. The GoF3 builder measures a nozzle by the hottest texels of its glow, a core of 1 to
        /// 5 x less than the nozzle (the Atauno Rheep's 16 units in its ~85-unit hexagon), while the game's ships' and EVERSPACE
        /// 2's nozzles are measured whole: a style was a fifth of its size on a GoF3 hull, and a GoF3 hull wearing its own
        /// race's style now shows it as large as its own effect. Without "nozzle" the shapes are kept (a mod written before it:
        /// the exhaust's width as every engine's nozzle made the GoF3 Vossk hulls' styles three times too big).</summary>
        static NozzleShape[] FitToOwnEffect(int ship, List<WeaponMount> exhausts, NozzleShape[] shapes)
        {
            var fx = CustomShips.Get(ship)?.engineFx;
            if (fx == null || !fx.Any || fx.nozzle <= 0f || shapes.Length == 0) return shapes;
            var perScale = new List<float>();
            for (int i = 0; i < shapes.Length; i++)
                if (shapes[i] != null && shapes[i].Mean > 0f) perScale.Add(shapes[i].Mean / FxScale(exhausts[i]));
            if (perScale.Count == 0) return shapes;
            perScale.Sort();
            float median = perScale.Count % 2 == 1 ? perScale[perScale.Count / 2]
                                                   : 0.5f * (perScale[perScale.Count / 2 - 1] + perScale[perScale.Count / 2]);
            float k = fx.NozzleRadius / median;
            for (int i = 0; i < shapes.Length; i++) if (shapes[i] != null) shapes[i] = shapes[i].Scaled(k);
            return shapes;
        }

        static float FxScale(WeaponMount m) => m.fxScale > 0f ? m.fxScale : 1f;

        /// <summary>Whether a mount's "outline" can stand for its nozzle: against the mount's glowSize (when it has one) at least
        /// half its size and its centre within 1.5 x it (a nozzle may sit a little off its mount: the effect is centred on it).
        /// The GoF3 builder's Vossk outlines came from a few lit pixels of their nozzle rings: slivers a fifth of the nozzle
        /// across (the Shaan'ra-Grth's 1 unit wide, 18 units off its mount), which shrank a style to nothing.</summary>
        static bool Plausible(NozzleShape o, WeaponMount m)
        {
            if (o == null) return false;
            if (m.glowSize == null || m.glowSize.Length < 2 || m.glowSize[0] <= 0f || m.glowSize[1] <= 0f) return o.Mean >= MinHalf;
            float glow = Mathf.Sqrt(m.glowSize[0] * m.glowSize[1]);
            float off = new Vector2(o.centreX, o.centreY).magnitude / M;
            return o.Mean >= 0.5f * glow && off <= 1.5f * glow;
        }

        /// <summary>The exhaust size the player can pick (x the fitted size), and its steps.</summary>
        public const float MinSize = 0.5f, MaxSize = 2f, SizeStep = 0.1f;

        /// <summary>A build's exhaust size (1 for none or an unreadable one), within MinSize .. MaxSize.</summary>
        public static float ParseSize(string text) =>
            float.TryParse(text, System.Globalization.NumberStyles.Float, System.Globalization.CultureInfo.InvariantCulture, out float f) && f > 0f
                ? Mathf.Clamp(Mathf.Round(f / SizeStep) * SizeStep, MinSize, MaxSize) : 1f;

        /// <summary>A size as build text (null for 1, the fitted size).</summary>
        public static string SizeText(float size) =>
            Mathf.Abs(size - 1f) < 0.001f ? null : size.ToString("0.##", System.Globalization.CultureInfo.InvariantCulture);

        /// <summary>Whether 'ship' comes with 'style''s mod (its own boost jets: the boost cards are its).</summary>
        static bool OwnShip(int ship, Style style) =>
            ModContent.ShipKey(ship) is string key && key.StartsWith(style.mod.Id + ":", StringComparison.OrdinalIgnoreCase);

        /// <summary>A colour value for a style: one of its palette's ids or "#RRGGBB" (false: none, "none", or neither).</summary>
        public static bool ResolveColor(Style style, string value, out Color color)
        {
            color = Color.white;
            if (style == null || string.IsNullOrEmpty(value) || value == ModShipKits.Build.NoSkin) return false;
            if (value.StartsWith("#")) return ModShipKits.TryHex(value, out color);
            var choice = style.Colors.Find(c => string.Equals(c.id, value, StringComparison.OrdinalIgnoreCase));
            if (choice == null) return false;
            color = choice.color;
            return true;
        }

        // ---- on a ship ---------------------------------------------------------------------------------------------------

        /// <summary>Whether 't' is (under) a customized exhaust's holder.</summary>
        public static bool InHolder(Transform t)
        {
            for (; t != null; t = t.parent) if (t.name == HolderName) return true;
            return false;
        }

        /// <summary>Whether a ship model wears a customized exhaust (Apply made its holder).</summary>
        public static bool HasHolder(Transform model)
        {
            if (model == null) return false;
            foreach (var t in model.GetComponentsInChildren<Transform>(true)) if (t.name == HolderName) return true;
            return false;
        }

        /// <summary>The style nozzle 't' belongs to (null: none).</summary>
        public static Transform NozzleOf(Transform t)
        {
            for (; t != null; t = t.parent) if (t.name.StartsWith(NozzlePrefix, StringComparison.Ordinal)) return t;
            return null;
        }

        static bool IsJet(Transform t)
        {
            for (; t != null; t = t.parent) if (t.name.StartsWith("engine_glow_thruster", StringComparison.Ordinal)) return true;
            return false;
        }

        /// <summary>An exhaust's nozzle radius in game units: a mod mount's glowSize (its half width / height), a mod ship's
        /// engineGlowRadius, else a game ship's exhaust anchor scale (weapons_hd "turretAngles", ShipExhaust's particle size:
        /// 250 x it across).</summary>
        public static float NozzleRadius(int ship, WeaponMount m)
        {
            if (m.glowSize != null && m.glowSize.Length >= 2 && m.glowSize[0] > 0f) return Mathf.Max(m.glowSize[0], m.glowSize[1]);
            if (CustomShips.Get(ship) is CustomShipData c && c.engineGlowRadius > 0f) return c.engineGlowRadius;
            float s = m.turretAngles != null && m.turretAngles.Length > 0 && m.turretAngles[0] > 0f ? m.turretAngles[0] : 1f;
            return GameShipRadius * s;
        }

        /// <summary>A game ship's nozzle radius per unit of its exhaust anchor's scale (game units; matched by eye on the
        /// Phantom's, the Betty's and the Inflict's engines).</summary>
        public const float GameShipRadius = 55f;

        /// <summary>Puts 'build''s exhaust style (none: nothing) on 'model', an instance of 'ship''s model in the scene; 'mounts':
        /// the exhausts it shows (null: ModShipKits.MountsShown's for the build). Returns the holder (null: nothing done).</summary>
        public static GameObject Apply(Database db, int ship, GameObject model, ModShipKits.Build build, IList<WeaponMount> mounts = null)
        {
            var style = Find(build?.exhaust, ship);   // (a style the ship isn't offered: its own exhaust)
            if (style == null || model == null || db == null) return null;
            var asm = model.GetComponent<AssembledObject>() ?? model.GetComponentInChildren<AssembledObject>(true);
            var parts = asm != null ? asm.playerVariantParts : null;
            var glow = parts?.FirstOrDefault(p => p != null && p.name.Contains("engine_glow")) ?? parts?.FirstOrDefault(p => p != null);
            if (glow == null) return null;   // a ship without a player engine (a freighter, a capital hull)
            var exhausts = (mounts ?? ModShipKits.MountsShown(db, ship, 3, build) ?? new List<WeaponMount>()).Where(m => m != null && m.slotType == 3).ToList();
            if (exhausts.Count == 0) return null;
            if (style.IsFlame)
            {
                bool missing = false;
                foreach (var m in style.members.Count > 0 ? style.members : new List<Style> { style })
                    if (ModShips.PartTemplate(m.kit, m.model) == null) { ModShips.EnsureExhaust(m); missing = true; }   // ModShips.ModelsChanged follows: the ship is built again
                if (missing) return null;
            }

            // The model's own exhaust out of sight.
            foreach (var r in glow.GetComponentsInChildren<Renderer>(true))
                if (!InHolder(r.transform) && !IsJet(r.transform)) r.forceRenderingOff = true;
            foreach (var fx in model.GetComponentsInChildren<EngineFx>(true))
                if (!InHolder(fx.transform)) fx.enabled = false;
            foreach (var f in model.GetComponentsInChildren<EngineFlame>(true))
                if (!InHolder(f.transform)) f.suppressed = true;

            // The holder: under the glow part, aligned with the ship's root (the mounts are in the root's frame).
            foreach (Transform old in glow.transform) if (old.name == HolderName) UnityEngine.Object.Destroy(old.gameObject);
            var holder = new GameObject(HolderName);
            holder.transform.SetParent(glow.transform, false);
            holder.transform.SetPositionAndRotation(model.transform.position, model.transform.rotation);
            Vector3 ls = model.transform.lossyScale, ps = glow.transform.lossyScale;
            holder.transform.localScale = new Vector3(ps.x != 0f ? ls.x / ps.x : 1f, ps.y != 0f ? ls.y / ps.y : 1f, ps.z != 0f ? ls.z / ps.z : 1f);
            holder.layer = glow.layer;

            Color? main = ResolveColor(style, build.exhaustColor, out var mc) ? mc : (Color?)null;
            Color? second = ResolveColor(style, build.exhaustColor2, out var sc) ? sc : (Color?)null;
            float size = ParseSize(build.exhaustSize);   // the player's resizing
            var shapes = ShapesOf(ship, model, exhausts);
            var shapeOf = new Dictionary<WeaponMount, NozzleShape>();
            for (int i = 0; i < exhausts.Count; i++) shapeOf[exhausts[i]] = shapes[i];
            // Sized by the nozzle's mean radius against the style's, x the player's size; its outline gives the cross-section.
            float Scale(WeaponMount m) => Mathf.Clamp(shapeOf[m].Mean / style.nozzle, 0.1f, 6f) * size;
            Vector2 Centre(WeaponMount m) => new Vector2(shapeOf[m].centreX, shapeOf[m].centreY);
            if (!style.IsFlame)
            {
                // GoF3's own colours, not the ship's nozzle colours: it looks the same on every ship unless the build picks
                // others. As long for the ship as on GoF3's own hulls (EngineLength).
                float length = EngineLength(model, shapes);
                EngineFx.Create(holder, holder, style.fx, exhausts, style.mod, style.Key, Scale, main, second, Centre,
                                m => shapeOf[m].Normalized(), m => length);
                return holder;
            }
            var flame = holder.AddComponent<EngineFlame>();
            for (int i = 0; i < exhausts.Count; i++)
            {
                var m = exhausts[i];
                // a group's member made for the nearest nozzle size (the player's resizing counts)
                var member = style.ForNozzle(shapeOf[m].Mean * M * size);
                var template = ModShips.PartTemplate(member.kit, member.model);
                var yaw = Quaternion.Euler(0f, member.yaw, 0f);
                var go = UnityEngine.Object.Instantiate(template, holder.transform, false);
                go.name = NozzlePrefix + i + "_" + member.localId;   // (which of a group's thrusters it wears)
                go.SetActive(true);
                var turn = ModHardpoints.MountTurn(m);
                var c = Centre(m);
                var nozzle = WeaponSystem.MountToLocal(m) + turn * new Vector3(c.x, c.y, 0f);
                go.transform.localPosition = nozzle;
                go.transform.localRotation = turn * yaw;
                // The model's nozzle (radius 'nozzle', model units) at this one's mean radius, then its flame bent to this
                // nozzle's outline (Warp).
                float k = Mathf.Clamp(shapeOf[m].Mean * M / member.nozzle * size, 0.01f, 100f);
                go.transform.localScale = Vector3.one * k;
                foreach (var t in go.GetComponentsInChildren<Transform>(true)) t.gameObject.layer = glow.layer;
                Warp(go, holder.transform, nozzle, turn, shapeOf[m].Normalized(), flame.ownedMeshes);
                if (main.HasValue || second.HasValue) Recolour(member, go, main, second);
            }
            flame.boostOverride = !OwnShip(ship, style);
            return holder;
        }

        /// <summary>Galaxy on Fire 3's hulls carry nozzles of about this share of their length (their median, 0.062: the
        /// Moornta's 87 on 914 game units); the game's ships far smaller ones (the Phantom's 25 on 940).</summary>
        public const float Gof3NozzleToLength = 0.062f;

        /// <summary>How much longer a Galaxy on Fire 3 engine is drawn on 'model' than its nozzles' width alone makes it: GoF3
        /// sized its plumes for its own hulls' big nozzles, so on a hull whose nozzles are a smaller share of its length (the
        /// game's ships: the Phantom's a third of the Moornta's for the same length) the plume, fitted across the nozzle,
        /// came out short for the ship (a player's report on the Phantom, 2026-10-24); stretched by that share (1 on GoF3's
        /// own hulls, at most 3).</summary>
        static float EngineLength(GameObject model, NozzleShape[] shapes)
        {
            var means = shapes.Where(s => s != null && s.Mean > 0f).Select(s => s.Mean).OrderBy(x => x).ToList();
            if (means.Count == 0) return 1f;
            float nozzle = means[means.Count / 2];
            float length = World.PlayerHull.HullBounds(model.transform, model.transform).size.z / M;
            if (length <= 0f || nozzle <= 0f) return 1f;
            return Mathf.Clamp(Gof3NozzleToLength / (nozzle / length), 1f, 3f);
        }

        /// <summary>A flame model's cross-section bent to a nozzle's outline: each vertex's distance from the flame's axis (in
        /// the mount's plane) times the outline's factor at its angle (a square nozzle gets a square flame); its meshes copied
        /// (into 'owned', freed with the holder).</summary>
        static void Warp(GameObject go, Transform holder, Vector3 nozzle, Quaternion turn, float[] profile, List<Mesh> owned)
        {
            if (profile == null) return;
            var inv = Quaternion.Inverse(turn);
            foreach (var mf in go.GetComponentsInChildren<MeshFilter>(true))
            {
                var src = mf.sharedMesh;
                if (src == null || !src.isReadable) continue;
                var mesh = UnityEngine.Object.Instantiate(src);
                mesh.name = src.name + " (nozzle)";
                var toHolder = holder.worldToLocalMatrix * mf.transform.localToWorldMatrix;
                var back = toHolder.inverse;
                var v = mesh.vertices;
                for (int j = 0; j < v.Length; j++)
                {
                    var q = inv * (toHolder.MultiplyPoint3x4(v[j]) - nozzle);   // the mount's plane: x, y across, z along
                    float f = FactorOf(profile, Mathf.Atan2(q.y, q.x));
                    q.x *= f; q.y *= f;
                    v[j] = back.MultiplyPoint3x4(nozzle + turn * q);
                }
                mesh.vertices = v;
                mesh.RecalculateBounds();
                mf.sharedMesh = mesh;
                owned.Add(mesh);
            }
        }

        /// <summary>A flame style's flames in the build's colours: copies of its flame materials with the flame colour
        /// (_EmissionColor) and the boost colour (_BoostColor) at the materials' own intensity.</summary>
        static void Recolour(Style style, GameObject go, Color? main, Color? second)
        {
            foreach (var r in go.GetComponentsInChildren<Renderer>(true))
            {
                var mats = r.sharedMaterials;
                bool changed = false;
                for (int i = 0; i < mats.Length; i++)
                {
                    var m = mats[i];
                    if (!EngineFlame.IsFlame(m)) continue;
                    var key = (m, (Color32)(main ?? Color.clear), (Color32)(second ?? Color.clear));
                    if (!style.flameCopies.TryGetValue(key, out var copy) || copy == null)
                    {
                        copy = new Material(m) { name = m.name };
                        if (main.HasValue) copy.SetColor("_EmissionColor", Tinted(m.GetColor("_EmissionColor"), main.Value));
                        if (second.HasValue) copy.SetColor("_BoostColor", Tinted(m.GetColor("_BoostColor"), second.Value));
                        style.flameCopies[key] = copy;
                    }
                    mats[i] = copy;
                    changed = true;
                }
                if (changed) r.sharedMaterials = mats;
            }
        }

        /// <summary>'pick' (a palette colour) at the brightness of 'own' (an HDR colour: its intensity kept).</summary>
        static Color Tinted(Color own, Color pick)
        {
            float k = Mathf.Max(own.r, Mathf.Max(own.g, own.b));
            var c = pick.linear;
            float n = Mathf.Max(1e-4f, Mathf.Max(c.r, Mathf.Max(c.g, c.b)));
            return new Color(c.r / n * k, c.g / n * k, c.b / n * k, 1f);
        }
    }
}
