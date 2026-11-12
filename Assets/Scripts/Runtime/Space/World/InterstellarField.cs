// InterstellarField.cs
// Remake: the suns of the space between the systems (Data.InterstellarSpace, added by SpaceLevel): each system on the star
// map as its sun, the map's own sun material on a quad kept inside the far plane, a beacon from afar growing as the ship
// closes in. The warp itself is the sky's (Visuals.WarpSky). The space opens on the sky of the system left, and all the way
// across a leg the next system's sky crossfades in over it (GoF2/SpaceSky _SkyBlend, InterstellarSpace.SkyShare, the user: the
// sky shows the distance flown and the way out isn't a jump): what the autopilot flies to (a dive into the Void too,
// Navigation.VoidDive: no sun, its orbit seeping in as it nears), else the nearest, and a system the flight passes on its way.
// A system flown past stays: once the flight goes on to another its sky is the one to leave from (TakeOver). Near where the
// ship is going the rest of the orbit comes too (SystemApproach). The sky is turned so that the way to the system is the way
// the ship will face in the orbit, so the sky ahead is the same one on both sides of the load; past the closest point it
// holds still, as that way swings round.
// Remake (2026-11-03, the user: the sun first, its planets round it, one picked and flown to, its orbit spawning there): a
// system's planets, once the navigation has given them their places (Navigation.PlanetsLaidOut), are the star map's 3D
// planets (galaxymap_planet_*, GoF2/SpacePlanet: lit toward their own sun, fading), PlanetRadiusMeters x their map size,
// kept inside the far plane like the suns; they fade in as the system nears and out as the arrival's own orbit is built ahead
// (SystemApproach.HandedOver), its planet taking over. The approach is aimed at the arrival planet (Navigation.ArrivalPoint).
// They come out only in their system's zone (Navigation.Zone: within a light year of its star the space round the ship grows
// InterstellarSpace.ZoneScale times), orbiting their star on faint rings; the suns' size follows the space's scale, so the
// zone's edge changes nothing on screen, and the approach and the sky's distances are the zone's and the hops' own.

using System.Collections.Generic;
using GoF2Remake.Data;
using GoF2Remake.Flight;
using GoF2Remake.UI;
using GoF2Remake.Visuals;
using UnityEngine;

namespace GoF2Remake.World
{
    public class InterstellarField : MonoBehaviour
    {
        /// <summary>A sun's size in the space, and how small and large it may look (half its angle, radians).</summary>
        const float SunRadiusMeters = 3000f, MinAngle = 0.05f, MaxAngle = 0.35f;

        /// <summary>How fast a system's sky may change (per ms).</summary>
        const float BlendPerMs = 1f / 1500f, M = 0.05f;
        /// <summary>How fast the system's sky may turn with the way to it (degrees per ms): a change of course, not a jump.</summary>
        const float SkyTurnPerMs = 0.006f;
        /// <summary>A sky more than this far in when the flight turns to another system is kept and left from; less fades back.</summary>
        const float KeepShare = 0.5f;

        struct Sun { public Navigation.Target target; public Transform quad; }

        /// <summary>A planet's radius in its zone for a map planet of the baseline size (GalaxyMap.SystemLayout's scale
        /// 256 x 16 / 65536), and how far into the zone (of its size) the planets are all there, faded in from its edge.</summary>
        const float PlanetRadiusMeters = 600f, PlanetBaseScale = 256f * 16f / 65536f, PlanetsInBy = 0.96f;
        /// <summary>The orbit rings: points round, their width (of the distance) and colour.</summary>
        const int RingPoints = 96;
        const float RingWidth = 0.003f;
        static readonly Color RingColour = new Color(0.7f, 0.85f, 1f, 0.55f);
        sealed class Body { public Navigation.Target target; public Transform model, disc; public float meshRadius, meters; public List<Material> looks; public Material discLook; public LineRenderer ring; }
        Material ringLook;
        readonly List<Body> bodies = new List<Body>();
        static Shader planetShader;

        readonly List<Sun> suns = new List<Sun>();
        readonly Dictionary<int, Material> skies = new Dictionary<int, Material>();
        readonly Dictionary<int, Matrix4x4> skyRotations = new Dictionary<int, Matrix4x4>();
        SpaceLevel level;
        Navigation nav;
        Transform ship;
        Database db;
        int origin;
        Vector3 originAt;
        Navigation.Target blended;
        float blend, approachBlend;
        Quaternion skyTurn = Quaternion.identity;
        OrbitLayout blendedLayout;
        int loadedStation = -1;
        Vector3 arrivalAt;
        Quaternion arrivalFacing = Quaternion.identity;
        Matrix4x4 blendedSky = Matrix4x4.identity;
        SystemApproach approach;

        public void Setup(SpaceLevel level)
        {
            this.level = level;
            nav = level.Navigation;
            ship = level.Player.transform;
            db = level.Database;
            // out of the Void: beside the system it was entered from (InterstellarSpace.PositionOf)
            int left = level.Layout.stationIndex == Session.VoidOrbit ? Session.VoidReturnStation : level.Layout.stationIndex;
            origin = db.Stations.Find(s => s.index == left)?.system ?? level.Layout.systemIndex;
            originAt = ship.position;
            foreach (var t in nav.Targets) if (t.kind == Navigation.Kind.System && t.planetOf == null && t.system == origin) originAt = t.Position;
            var materials = StarMapAssets.Load()?.sunMaterials;
            var mesh = Quad();
            foreach (var t in level.Navigation.Targets)
            {
                if (t.kind != Navigation.Kind.System || t.planetOf != null) continue;   // a planet pick has no sun of its own
                var go = new GameObject("Sun " + t.name);
                go.transform.SetParent(transform, false);
                go.AddComponent<MeshFilter>().sharedMesh = mesh;
                var r = go.AddComponent<MeshRenderer>();
                int texture = level.Database.Systems.Find(s => s.index == t.system)?.textureIndex ?? 0;
                if (materials != null && materials.Length > 0) r.sharedMaterial = materials[texture >= 0 && texture < materials.Length ? texture : 0];
                r.shadowCastingMode = UnityEngine.Rendering.ShadowCastingMode.Off;
                r.receiveShadows = false;
                suns.Add(new Sun { target = t, quad = go.transform });
            }
            foreach (var t in level.Navigation.Targets)
                if (t.kind == Navigation.Kind.System && t.planetOf != null) AddBody(t);
            nav.WorldScaled += (pivot, k) => originAt = pivot + (originAt - pivot) * k;   // a zone's edge: the point left follows
        }

        /// <summary>A planet's orbit ring round its star (drawn in UpdateBodies, in the disc's plane through the planet).</summary>
        LineRenderer Ring(Navigation.Target t)
        {
            if (ringLook == null)
            {
                var shader = Shader.Find("Sprites/Default") ?? Shader.Find("Universal Render Pipeline/Unlit");
                if (shader == null) return null;
                ringLook = new Material(shader) { name = "Orbit ring" };
                ringLook.color = RingColour;
            }
            var go = new GameObject("Orbit " + t.name);
            go.transform.SetParent(transform, false);
            var line = go.AddComponent<LineRenderer>();
            line.useWorldSpace = true;
            line.loop = true;
            line.positionCount = RingPoints;
            line.sharedMaterial = ringLook;
            line.shadowCastingMode = UnityEngine.Rendering.ShadowCastingMode.Off;
            line.receiveShadows = false;
            line.startColor = line.endColor = RingColour;
            go.SetActive(false);
            return line;
        }

        /// <summary>The planet target's 3D planet: the star map's model for its station's texture, its lit materials GoF2/SpacePlanet
        /// copies, hidden until its system's planets have their places.</summary>
        void AddBody(Navigation.Target t)
        {
            var station = db.Stations.Find(s => s.index == t.station);
            int tex = station?.textureIndex ?? 0;
            var prefab = AssembledObject.LoadPrefab(db.AssemblyByName($"galaxymap_planet_{tex:000}")) ?? AssembledObject.LoadPrefab(db.AssemblyByName("galaxymap_planet_000"));
            if (prefab == null) return;
            var go = Instantiate(prefab, transform, false);
            go.name = "Planet " + t.name;
            GunRig.StripForFx(go);
            var looks = SpaceLooks(go);
            var disc = ModLook(go, t.station, looks, out var discLook);   // a mod station's own planet, as the star map shows it
            float radius = 0f;
            foreach (var mf in go.GetComponentsInChildren<MeshFilter>(true))
                if (mf.sharedMesh != null) radius = Mathf.Max(radius, mf.sharedMesh.bounds.extents.magnitude * mf.transform.lossyScale.x / Mathf.Max(1e-6f, go.transform.lossyScale.x));
            var sys = db.Systems.Find(s => s.index == t.system);
            var layout = GalaxyMap.SystemLayout(db, sys);
            int k = layout.FindIndex(p => p.station == t.station);
            float meters = PlanetRadiusMeters * (k >= 0 && layout[k].scale > 0f ? layout[k].scale / PlanetBaseScale : 1f);
            go.SetActive(false);
            bodies.Add(new Body { target = t, model = go.transform, disc = disc, discLook = discLook, meshRadius = Mathf.Max(1e-3f, radius), meters = meters, looks = looks, ring = Ring(t) });
        }

        /// <summary>Remake mods, as the star map's system view (UI.StarMap.ApplyModPlanet): a station's "mapTexture" wrapped round
        /// a UV sphere in the model's place (still GoF2/SpacePlanet, lit toward its sun), else its "planetTexture" as the orbit's
        /// disc facing the camera (the mods' alpha fx material, unlit); null and nothing changed for the game's planets.</summary>
        static Transform ModLook(GameObject planet, int station, List<Material> looks, out Material discLook)
        {
            discLook = null;
            string wrap = Modding.ModWorld.MapTexture(station), discName = wrap == null ? Modding.ModWorld.PlanetTexture(station) : null;
            if (wrap == null && discName == null) return null;
            var hull = new List<MeshRenderer>();
            foreach (var r in planet.GetComponentsInChildren<MeshRenderer>(true))
                if (r.name.StartsWith("map_planet_") && !r.name.StartsWith("map_planet_ring")) hull.Add(r);
            if (hull.Count == 0) return null;
            var tex = Modding.ModBackdrop.Texture(wrap ?? discName);
            if (tex == null) return null;
            if (wrap != null)
            {
                tex.wrapModeU = TextureWrapMode.Repeat;
                foreach (var r in hull)
                {
                    var mf = r.GetComponent<MeshFilter>();
                    if (mf != null && mf.sharedMesh != null) mf.sharedMesh = PlanetMeshes.WrappedSphere(mf.sharedMesh.bounds.extents.x);
                    foreach (var m in r.sharedMaterials)
                        if (m != null && looks.Contains(m)) { m.SetTexture("_BaseMap", tex); m.SetTextureScale("_BaseMap", Vector2.one); m.SetTextureOffset("_BaseMap", Vector2.zero); m.SetColor("_BaseColor", Color.white); }
                }
                return null;
            }
            var template = Modding.ModAssets.Get()?.fxAlpha;
            if (template == null) return null;
            discLook = new Material(template) { name = "space planet disc " + discName };
            discLook.SetTexture("_MainTex", tex);
            discLook.SetTextureScale("_MainTex", Vector2.one);
            discLook.SetTextureOffset("_MainTex", Vector2.zero);
            discLook.SetColor("_Color", Color.white);
            var mesh = hull[0].GetComponent<MeshFilter>()?.sharedMesh;
            float radius = mesh != null ? mesh.bounds.extents.x : 1600f;
            foreach (var r in hull) r.enabled = false;
            var go = new GameObject("space planet disc");
            go.transform.SetParent(planet.transform, false);
            go.transform.localScale = Vector3.one * radius * 2.1f;   // the orbit images keep a little room round the disc
            go.AddComponent<MeshFilter>().sharedMesh = PlanetMeshes.DiscQuad();
            go.AddComponent<MeshRenderer>().sharedMaterial = discLook;
            return go.transform;
        }

        /// <summary>A planet's orbit: the circle round its star in its disc through it, each point kept inside the far plane along
        /// its own way from the eye (as the suns are: the same on screen), its width a share of the distance.</summary>
        void DrawRing(LineRenderer ring, Navigation.Target sun, Navigation.Target planet, Vector3 eye, float inner, float fade)
        {
            if (!nav.PlanetDisc(sun, out var right, out var depth)) return;
            var c = sun.Position;
            float r = (planet.Position - c).magnitude;
            for (int i = 0; i < RingPoints; i++)
            {
                float a = i * Mathf.PI * 2f / RingPoints;
                var to = c + (right * Mathf.Sin(a) + depth * Mathf.Cos(a)) * r - eye;
                float d = Mathf.Max(1f, to.magnitude);
                ring.SetPosition(i, eye + to / d * Mathf.Min(d, inner));
            }
            ring.widthMultiplier = Mathf.Min((c - eye).magnitude, inner) * RingWidth;
            var col = new Color(RingColour.r, RingColour.g, RingColour.b, RingColour.a * fade);
            ring.startColor = ring.endColor = col;
        }

        /// <summary>The model's lit materials (the planet's surface, not a glow or a ring) as GoF2/SpacePlanet copies.</summary>
        static List<Material> SpaceLooks(GameObject go)
        {
            var list = new List<Material>();
            if (planetShader == null) planetShader = Resources.Load<Shader>("GoF2Sky/SpacePlanet");
            if (planetShader == null) return list;
            foreach (var r in go.GetComponentsInChildren<MeshRenderer>(true))
            {
                var mats = r.sharedMaterials;
                for (int i = 0; i < mats.Length; i++)
                {
                    var src = mats[i];
                    if (src == null || !(src.shader.name.Contains("Lit") || src.shader.name == "GoF2/Hull")) continue;
                    var m = new Material(planetShader) { name = src.name + " (space planet)" };
                    if (src.HasProperty("_BaseMap")) { m.SetTexture("_BaseMap", src.GetTexture("_BaseMap")); m.SetTextureScale("_BaseMap", src.GetTextureScale("_BaseMap")); m.SetTextureOffset("_BaseMap", src.GetTextureOffset("_BaseMap")); }
                    if (src.HasProperty("_BaseColor")) m.SetColor("_BaseColor", src.GetColor("_BaseColor"));
                    mats[i] = m;
                    list.Add(m);
                }
                r.sharedMaterials = mats;
            }
            return list;
        }

        /// <summary>Every frame: each laid-out system's planets where they are (inside the far plane, as the suns), faded in with
        /// the distance and out as its arrival's orbit takes over.</summary>
        void UpdateBodies(Camera cam, float inner, Vector3 eye)
        {
            foreach (var b in bodies)
            {
                var sun = b.target.planetOf;
                float fade = 0f;
                if (sun != null && !sun.hidden && sun == nav.Zone && nav.PlanetsLaidOut(sun))
                {
                    float units = (sun.Position - eye).magnitude / M, zone = InterstellarSpace.ZoneUnits * InterstellarSpace.ZoneScale;
                    fade = Mathf.InverseLerp(zone, zone * PlanetsInBy, units);   // in as the zone opens, orbiting their star
                    if (sun == blended) fade *= 1f - SystemApproach.HandedOver(approachBlend);
                    fade *= level.HudShown;
                }
                bool shown = fade > 0.005f;
                if (b.model.gameObject.activeSelf != shown) b.model.gameObject.SetActive(shown);
                if (b.ring != null && b.ring.gameObject.activeSelf != shown) b.ring.gameObject.SetActive(shown);
                if (!shown) continue;
                if (b.ring != null) DrawRing(b.ring, sun, b.target, eye, inner, fade);
                var to = b.target.Position - eye;
                float distance = Mathf.Max(1f, to.magnitude), at = Mathf.Min(distance, inner);
                b.model.position = eye + to / distance * at;
                b.model.localScale = Vector3.one * (b.meters * at / distance / b.meshRadius);
                var sunDir = (sun.Position - b.target.Position).normalized;
                foreach (var m in b.looks) { m.SetVector("_SunDir", sunDir); m.SetFloat("_Fade", fade); }
                if (b.disc != null)
                {
                    b.disc.rotation = Quaternion.LookRotation(b.disc.position - eye, cam.transform.up);   // facing the camera
                    b.discLook.SetColor("_Color", new Color(1f, 1f, 1f, fade));
                }
            }
        }

        static Mesh Quad()
        {
            var m = new Mesh { name = "InterstellarSun" };
            m.vertices = new[] { new Vector3(-1f, -1f), new Vector3(1f, -1f), new Vector3(-1f, 1f), new Vector3(1f, 1f) };
            m.uv = new[] { new Vector2(0f, 0f), new Vector2(1f, 0f), new Vector2(0f, 1f), new Vector2(1f, 1f) };
            m.triangles = new[] { 0, 2, 1, 1, 2, 3 };
            m.bounds = new Bounds(Vector3.zero, Vector3.one * 2f);
            return m;
        }

        void LateUpdate()
        {
            var cam = Camera.main;
            if (cam == null) return;
            float inner = cam.farClipPlane * 0.5f;
            var eye = cam.transform.position;
            foreach (var s in suns)
            {
                bool shown = !s.target.hidden;
                if (s.quad.gameObject.activeSelf != shown) s.quad.gameObject.SetActive(shown);
                if (!shown) continue;
                var to = s.target.Position - eye;
                float distance = Mathf.Max(1f, to.magnitude), at = Mathf.Min(distance, inner);
                s.quad.SetPositionAndRotation(eye + to / distance * at, cam.transform.rotation);
                // the star ahead gives way to the orbit's own sun (SystemApproach), the others early to the orbit's sky
                float left = s.target == blended ? 1f - SystemApproach.SunShown(approachBlend) : 1f - SystemApproach.HandedOver(approachBlend);
                left *= level.HudShown;   // none of them in the hole the ship came through: they come in after it, with the HUD
                // its size follows the space's: in a zone the star is its own size, outside its zone shrunk with the hops' scale
                float radius = SunRadiusMeters * nav.WorldScale / InterstellarSpace.ZoneScale;
                s.quad.localScale = Vector3.one * (at * left * Mathf.Clamp(radius / distance, MinAngle, MaxAngle));
            }
            UpdateBodies(cam, inner, eye);
            UpdateSky(Time.deltaTime * 1000f);
        }

        void UpdateSky(float dtMs)
        {
            if (ship == null) return;
            var want = NextSystem();
            float goal = 0f, near = 0f;
            if (want != null)
            {
                float units = (want.Position - ship.position).magnitude / M / nav.WorldScale;   // at the hops' scale
                float toArrival = (nav.ArrivalPoint(want) - ship.position).magnitude / M;   // its arrival planet once laid out
                // A system the autopilot only flies past shows its sky; its orbit comes only where the ship can arrive: in its zone.
                if ((want == nav.Zone || want == nav.VoidDive) && (nav.AutopilotTarget == null || want == Navigation.SunOf(nav.AutopilotTarget)))   // (the Void has no zone: its dive seeps it in)
                    near = 1f - Mathf.SmoothStep(0f, 1f, Mathf.InverseLerp(InterstellarSpace.ArriveUnits, InterstellarSpace.ApproachUnits, toArrival));
                goal = Mathf.Max(near, InterstellarSpace.SkyShare((originAt - ship.position).magnitude / M / nav.WorldScale, units));
            }
            if (want != blended)
            {
                near = 0f;
                bool keep = blended != null && blended != nav.VoidDive && blend >= KeepShare;   // a dive called off leaves no Void behind
                goal = keep ? 1f : 0f;
                if (keep && blend >= 0.999f && approach == null) TakeOver();
                if (blended == null || (!keep && blend <= 0.001f))
                {
                    blended = want;
                    if (want != null) Load(want);
                    blend = 0f;
                }
            }
            blend = Mathf.MoveTowards(blend, goal, BlendPerMs * dtMs);
            approachBlend = Mathf.MoveTowards(approachBlend, near, BlendPerMs * dtMs);
            Shader.SetGlobalFloat("_SkyBlend", blended != null ? blend : 0f);
            if (approachBlend <= 0.001f && near <= 0f) { approach?.Remove(); approach = null; }
            nav.ApproachRuns = approach != null || near > 0f;   // no encounter pulls the ship out with an orbit built ahead
            if (blended == null) return;
            var way = nav.ArrivalPoint(blended) - ship.position;   // the orbit is built round the arrival planet
            if (way.sqrMagnitude < 1f) return;
            var turn = arrivalFacing * Quaternion.Inverse(Quaternion.LookRotation(way.normalized, Vector3.up));
            if (blend <= 0.001f) skyTurn = turn;
            else if (Navigation.SunOf(nav.AutopilotTarget) == blended || Vector3.Dot(way, ship.forward) > 0f) skyTurn = Quaternion.RotateTowards(skyTurn, turn, SkyTurnPerMs * dtMs);
            Shader.SetGlobalMatrix("_BlendRotation", blendedSky * Matrix4x4.Rotate(skyTurn));
            // the arrival the system has now (a planet picked, the programmed one): its orbit, until the approach is built
            if (approach == null && blended.station != loadedStation) Load(blended);
            if (approach == null && near > 0f && want == blended && level.HudShown > 0f) approach = new SystemApproach(level, blendedLayout, arrivalAt, arrivalFacing, transform);
            approach?.Update(ship.position, way, turn, approachBlend * level.HudShown);
        }

        /// <summary>The system whose sky comes next: the one the autopilot flies to, unless the flight passes another first; else
        /// the nearest. Never the one the sky is already.</summary>
        Navigation.Target NextSystem()
        {
            Navigation.Target nearest = null;
            float best = float.MaxValue;
            foreach (var s in suns)
            {
                if (s.target.hidden || s.target.system == origin) continue;
                float d = (s.target.Position - ship.position).sqrMagnitude;
                if (d < best) { best = d; nearest = s.target; }
            }
            var pilot = Navigation.SunOf(nav.AutopilotTarget);
            var aim = pilot != null && pilot.kind == Navigation.Kind.System && (pilot == nav.VoidDive || pilot.system != origin) ? pilot : null;
            if (aim == null) return nearest;
            float close = InterstellarSpace.ApproachUnits * M * nav.WorldScale;
            return nearest != null && nearest != aim && best < close * close && (aim.Position - ship.position).sqrMagnitude > close * close ? nearest : aim;
        }

        /// <summary>The flight has gone on from the system blended in: its sky is now the sky, turned as it was seen, and the next
        /// crossfades from it. Nothing changes on screen: the blend was full.</summary>
        void TakeOver()
        {
            origin = blended.system;
            originAt = blended.Position;
            if (skies.TryGetValue(blended.system, out var sky) && sky != null)
            {
                sky.SetMatrix("_SkyRotation", blendedSky * Matrix4x4.Rotate(skyTurn));
                RenderSettings.skybox = sky;
            }
            Shader.SetGlobalFloat("_SkyBlend", 0f);
            blended = null;
            blend = 0f;
        }

        /// <summary>The system's sky (as its arrival orbit builds it) into the shader's second set of layers.</summary>
        void Load(Navigation.Target system)
        {
            loadedStation = system.station;
            var layout = blendedLayout = OrbitLayout.Build(db, system.station);
            layout.ArrivalPose(Session.StationIndex, out arrivalAt, out arrivalFacing);
            if (layout.alienOrbit) Session.WarpArrivalPoint = layout.hiddenJumpgate;   // drawn at random: the arrival keeps this one
            if (!skies.TryGetValue(system.system, out var sky))
            {
                skies[system.system] = sky = OrbitBuilder.MakeSky(layout);
                if (sky != null) skyRotations[system.system] = sky.GetMatrix("_SkyRotation");
            }
            if (sky == null) return;
            blendedSky = skyRotations[system.system];
            LoadBlendSky(sky);
        }

        /// <summary>'sky' into the shader's second set of layers.</summary>
        public static void LoadBlendSky(Material sky)
        {
            Shader.SetGlobalTexture("_BlendStars", sky.GetTexture("_Stars"));
            Shader.SetGlobalTexture("_BlendNebula", sky.GetTexture("_Nebula"));
            SetMap(sky, "_StarsMap", "_BlendStarsMap", "_BlendStarsTexel");
            SetMap(sky, "_NebulaMap", "_BlendNebulaMap", "_BlendNebulaTexel");
            Shader.SetGlobalFloat("_BlendStarsMode", Mode(sky, "_STARS_PANORAMA", "_STARS_STRIP"));
            Shader.SetGlobalFloat("_BlendNebulaMode", Mode(sky, "_NEBULA_PANORAMA", "_NEBULA_STRIP"));
            Shader.SetGlobalFloat("_BlendStarsGain", sky.GetFloat("_StarsGain"));
            Shader.SetGlobalFloat("_BlendNebulaGain", sky.GetFloat("_NebulaGain"));
            Shader.SetGlobalFloat("_BlendExposure", sky.GetFloat("_Exposure"));
        }

        static void SetMap(Material sky, string from, string to, string texel)
        {
            var map = sky.GetTexture(from);
            if (map == null) return;
            Shader.SetGlobalTexture(to, map);
            Shader.SetGlobalVector(texel, new Vector4(1f / map.width, 1f / map.height, map.width, map.height));
        }

        static float Mode(Material sky, string panorama, string strip) => sky.IsKeywordEnabled(panorama) ? 1f : sky.IsKeywordEnabled(strip) ? 2f : 0f;

        void OnDisable() => Shader.SetGlobalFloat("_SkyBlend", 0f);

        void OnDestroy()
        {
            foreach (var b in bodies) { foreach (var m in b.looks) if (m != null) Destroy(m); if (b.discLook != null) Destroy(b.discLook); }
            if (ringLook != null) Destroy(ringLook);
            approach?.Remove();
            foreach (var sky in skies.Values) if (sky != null && sky != RenderSettings.skybox) Destroy(sky);
        }
    }
}
