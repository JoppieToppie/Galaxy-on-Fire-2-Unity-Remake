// Backdrop.cs
// Sun, planets, planet rings and the sun streak of the current orbit (StarSystem::render 0x15db84,
// renderSunStreak). They are quads on the original's `plane` (65000 units wide) kept 20000 units (1000 m) from the
// camera every frame, so they stay at infinity; the GoF2/Backdrop shader draws them behind all geometry.
//   Sun: additive, faces the camera with the camera's up, swells by e = max((I - 10) / 64, 0) where I is the lens
//        flare intensity 64 * (1 - d / (H / 2)) (d = sun distance from the screen centre in pixels).
//   Streak: the sun texture stretched horizontally by e (invisible when the sun is off-centre).
//   The supernova system (StarSystem+0xc, OrbitLayout.supernovaSun): the sun faces the camera with game +x as its up (no
//        roll with the camera) and never swells; the streak layer (sn_sun_011) is drawn twice: right after the sun as a
//        glow of 0.3 s + e facing the camera with the camera's up, then as the streak (that glow scaled again). After the
//        explosion the ring takes the sun's part and the core mesh the streak layer's (both drawings).
//   Planets: fixed orientation (not billboards), alpha blended, mirrored so the lit rim faces the sun; the orbit
//        planet (straight ahead, Unity +Z) grows up to +0.2 scale as the camera flies toward it.
//   Rings: the planet's quad x4 with sn_planet_ring.
// Materials come from Resources/GoF2Backdrop/<texture> (made by GoF2 > Scenes > Space Scene), so only the current
// orbit's textures are loaded.

using System.Collections.Generic;
using UnityEngine;
using UnityEngine.Rendering;

namespace GoF2Remake.World
{
    [Unity.Scripting.LifecycleManagement.NoAutoStaticsCleanup]
    public class Backdrop : MonoBehaviour
    {
        public const string MaterialFolder = "GoF2Backdrop";
        const float Distance = OrbitLayout.BackdropDistance * OrbitLayout.MetersPerUnit;   // 1000 m
        const float QuadMeters = OrbitLayout.PlaneSize * OrbitLayout.MetersPerUnit;        // 3250 m at scale 1

        [Tooltip("HDR multiplier on the sun's near-white core so it blooms under the remake's bloom (the original bloom and no bloom draw it at 1).")]
        public float sunGlow = 1.6f;
        int sunGlowStyle = -1;

        class Body
        {
            public Transform t;
            public Vector3 dir;          // Unity direction from the camera
            public Quaternion rot;       // fixed orientation (planets)
            public float scale;
            public bool orbitPlanet;
        }

        Camera cam;
        OrbitLayout layout;
        Body sun, streak, glow;
        readonly List<Body> planets = new List<Body>();

        /// <summary>StarSystem::getPlanetTargets: one per planet billboard with its station (the orbit planet = the current
        /// station's own, never lockable). The transforms follow the camera, 1000 m out.</summary>
        public readonly List<(int station, Transform transform, bool orbitPlanet)> PlanetTargets = new List<(int, Transform, bool)>();
        readonly List<Body> rings = new List<Body>();
        float flareIntensity;

        /// <summary>StarSystem::render2D: the sun projects in front and within -W..2W, -H..2H (the flare is drawn).</summary>
        public bool FlareVisible { get; private set; }
        /// <summary>LensFlare+0: K * (1 - d / (H / 2)), K = 64 (80 for flare colour 5).</summary>
        public float FlareIntensity => flareIntensity;
        /// <summary>DAT_002589ac[system]: the flare colour index.</summary>
        public int FlareColor => layout != null ? layout.flareColor : 3;
        /// <summary>The sun's screen position (pixels, origin bottom-left).</summary>
        public Vector2 SunScreen { get; private set; }
        static Mesh quad;

        public void Build(OrbitLayout orbit, Camera camera)
        {
            layout = orbit;
            cam = camera;
            var sunDir = OrbitLayout.DirToUnity(orbit.lightDirection).normalized;
            var sunMat = Load(orbit.sunTexture);
            sun = Make("Sun", sunMat, 2900, sunDir, Quaternion.identity, orbit.sunScale);
            // StarSystem::renderSunStreak: the supernova system draws its streak with sn_sun_011 (StarSystem+0x10, 0x2dde).
            var streakMat = orbit.supernovaSun ? Load("sn_sun_011") ?? sunMat : sunMat;
            streak = Make("SunStreak", streakMat, 2903, sunDir, Quaternion.identity, 0f);
            // The supernova system's glow (StarSystem::render: renderSunStreak right after the sun). Additive like the sun,
            // so the shared queue doesn't matter.
            if (orbit.supernovaSun) glow = Make("SunGlow", streakMat, 2900, sunDir, Quaternion.identity, 0f);
            sunGlowStyle = -1;
            ApplySunGlow();

            Color tint = orbit.fog ? (orbit.systemTexture == 15 ? orbit.fogColor : orbit.fogColor * 0.7f) : Color.clear;
            var ringMat = orbit.planets.Exists(p => p.ring) ? Load("sn_planet_ring") : null;
            foreach (var p in orbit.planets)
            {
                // StarSystem: rotation (pitch, yaw, 0) then moveForward(-20000), a quad facing the origin.
                var gameDir = OrbitLayout.Direction(p.pitch, p.yaw);
                var dir = OrbitLayout.DirToUnity(-gameDir).normalized;
                var up = OrbitLayout.DirToUnity(new Vector3(0f, Mathf.Cos(p.pitch), Mathf.Sin(p.pitch)));
                var rot = Quaternion.LookRotation(dir, up);
                // The planet PNGs have their lit rim on the right: mirror when the sun is on the left as seen.
                bool mirror = Vector3.Dot(sunDir, rot * Vector3.right) < 0f;
                var body = Make(p.texture, Load(p.texture), 2901, dir, rot, p.scale);
                body.orbitPlanet = p.isOrbitPlanet;
                SetProps(body.t, mirror, tint);
                planets.Add(body);
                PlanetTargets.Add((p.station, body.t, p.isOrbitPlanet));
                if (p.ring && ringMat != null)
                {
                    var ring = Make(p.texture + "_ring", ringMat, 2902, dir, rot, p.scale * 4f);
                    SetProps(ring.t, mirror, Color.clear);
                    rings.Add(ring);
                }
            }
        }

        /// <summary>A level script's scale on the sun billboard (the supernova cutscenes: planets[0] x0.95 / x4 per frame).</summary>
        [System.NonSerialized] public float sunScaleFactor = 1f;

        // ---- the supernova explosion (StarSystem::switchSunForSupernovaIntro 0x15d904, scaleSunDuringSupernovaIntro, updateSupernova)

        enum NovaPart { Ring, CoreGlow, CoreStreak }
        readonly List<(Transform t, float norm, NovaPart kind)> supernova = new List<(Transform, float, NovaPart)>();
        float supernovaRing;

        /// <summary>The sun becomes the explosion: planets[0] the ring mesh 0x2df1 at a uniform 0.68665 with texture 0x2df3
        /// (sn_sun_explosion_core), the streak layer the core mesh 0x2df2 with 0x2df4 (sn_sun_explosion_ring), both animations
        /// restarted; StarSystem::render then sizes them from the ring's scale like the sun's (the core 0.3 x the ring + e,
        /// drawn as the glow and as the streak). Remake: the converted meshes are ~4 m across where the plane quad is 3250 m,
        /// so they are scaled by that ratio to keep the original's proportions; far-plane additive like the sky layers.</summary>
        public void StartSupernovaExplosion(GameObject ringPrefab, Texture ringTexture, GameObject corePrefab, Texture coreTexture, Material template)
        {
            if (template == null) return;
            foreach (var b in new[] { sun, streak, glow }) if (b != null) b.t.gameObject.SetActive(false);
            supernovaRing = 0.6866455078125f;
            Add(ringPrefab, ringTexture, 2900, NovaPart.Ring);
            Add(corePrefab, coreTexture, 2900, NovaPart.CoreGlow);
            Add(corePrefab, coreTexture, 2903, NovaPart.CoreStreak);

            void Add(GameObject prefab, Texture tex, int queue, NovaPart kind)
            {
                if (prefab == null) return;
                var go = Instantiate(prefab, transform, false);
                var size = new Bounds(); bool first = true;
                foreach (var mf in go.GetComponentsInChildren<MeshFilter>(true))
                    if (mf.sharedMesh != null) { if (first) { size = mf.sharedMesh.bounds; first = false; } else size.Encapsulate(mf.sharedMesh.bounds); }
                float norm = QuadMeters / Mathf.Max(0.01f, Mathf.Max(size.size.x, size.size.y));
                foreach (var r in go.GetComponentsInChildren<Renderer>(true))
                {
                    var m = new Material(template) { renderQueue = queue };
                    m.SetTexture("_MainTex", tex);
                    m.SetFloat("_UseVertexColor", 0f);
                    r.sharedMaterial = m;
                    r.shadowCastingMode = UnityEngine.Rendering.ShadowCastingMode.Off;
                }
                foreach (var a in go.GetComponentsInChildren<Visuals.PartAnimation>(true)) a.applyMaterialChannels = true;
                Visuals.PartAnimation.PlayOnce(go);
                supernova.Add((go.transform, norm, kind));
            }
        }

        /// <summary>scaleSunDuringSupernovaIntro: the ring grows by 4e-5 per ms.</summary>
        public void GrowSupernova(float dtMs)
        {
            if (supernova.Count == 0) return;
            supernovaRing += 4e-5f * dtMs;
        }

        /// <summary>StarSystem::switchPlanetForIntro 0x15d820 (the prologue's time jump): the orbit planet gets planet_000_big
        /// and twice its size.</summary>
        public void SwitchOrbitPlanetForIntro()
        {
            var mat = Load("planet_000_big");
            foreach (var p in planets)
            {
                if (!p.orbitPlanet) continue;
                // StarSystem::switchPlanetForIntro also doubles the scaling, but StarSystem::render sets the orbit planet's
                // scale to +0x58 + zoom every frame (0x15de6c), so the x2 never shows: the size stays.
                if (mat != null) p.t.GetComponent<MeshRenderer>().sharedMaterial = mat;
            }
        }

        static void SetProps(Transform t, bool mirror, Color tint)
        {
            var block = new MaterialPropertyBlock();
            block.SetFloat("_Mirror", mirror ? 1f : 0f);
            block.SetColor("_Tint", tint);
            t.GetComponent<MeshRenderer>().SetPropertyBlock(block);
        }

        static Material Load(string texture)
        {
            var m = Resources.Load<Material>($"{MaterialFolder}/{texture}");
            if (m == null) Debug.LogWarning($"Backdrop: missing material Resources/{MaterialFolder}/{texture}");
            return m;
        }

        Body Make(string name, Material mat, int queue, Vector3 dir, Quaternion rot, float scale)
        {
            var go = new GameObject(name);
            go.transform.SetParent(transform, false);
            go.AddComponent<MeshFilter>().sharedMesh = Quad;
            var r = go.AddComponent<MeshRenderer>();
            r.sharedMaterial = mat;
            r.shadowCastingMode = UnityEngine.Rendering.ShadowCastingMode.Off;
            r.receiveShadows = false;
            r.lightProbeUsage = UnityEngine.Rendering.LightProbeUsage.Off;
            r.reflectionProbeUsage = UnityEngine.Rendering.ReflectionProbeUsage.Off;
            if (mat != null && mat.renderQueue != queue) r.material.renderQueue = queue;   // painter's order
            return new Body { t = go.transform, dir = dir, rot = rot, scale = scale };
        }

        static Mesh Quad
        {
            get
            {
                if (quad != null) return quad;
                quad = new Mesh { name = "BackdropQuad" };
                quad.vertices = new[] { new Vector3(-0.5f, -0.5f, 0f), new Vector3(0.5f, -0.5f, 0f), new Vector3(0.5f, 0.5f, 0f), new Vector3(-0.5f, 0.5f, 0f) };
                quad.uv = new[] { new Vector2(0f, 0f), new Vector2(1f, 0f), new Vector2(1f, 1f), new Vector2(0f, 1f) };
                quad.triangles = new[] { 0, 2, 1, 0, 3, 2 };
                quad.bounds = new Bounds(Vector3.zero, Vector3.one * 1e5f);   // never frustum-culled by its own size
                return quad;
            }
        }

        // Placed again right before the camera renders: the chase / cutscene cameras move in their own LateUpdate, which
        // may run after this one, and the bodies then trailed the camera by a frame (a stutter at fast-forward's x5).
        void OnEnable() => RenderPipelineManager.beginCameraRendering += OnBeginCamera;
        void OnDisable() => RenderPipelineManager.beginCameraRendering -= OnBeginCamera;
        void OnBeginCamera(ScriptableRenderContext context, Camera camera)
        {
            if (camera == cam) LateUpdate();
        }

        /// <summary>The sun layers' HDR core for the bloom option in force (the remake's bloom: sunGlow on the near-white
        /// texels; the original bloom and none: the texture as it is, like the original's additive sun). The whole quad at
        /// x1.6 had made the bright suns' halos (Mido's sun_009) far brighter and larger than the original's.</summary>
        void ApplySunGlow()
        {
            int style = Data.Settings.BloomStyle;
            if (style == sunGlowStyle) return;
            sunGlowStyle = style;
            float core = style == Data.Settings.BloomRemake ? sunGlow : 1f;
            foreach (var b in new[] { sun, streak, glow })
            {
                if (b == null) continue;
                var block = new MaterialPropertyBlock();
                block.SetColor("_Color", Color.white);
                block.SetFloat("_CoreGlow", core);
                b.t.GetComponent<MeshRenderer>().SetPropertyBlock(block);
            }
        }

        void LateUpdate()
        {
            if (cam == null || sun == null) return;
            ApplySunGlow();
            var c = cam.transform.position;

            // Lens flare intensity (StarSystem::render2D): from the sun's screen position, used for the swelling.
            flareIntensity = 0f;
            var sp = cam.WorldToScreenPoint(c + sun.dir * Distance);
            float W = Screen.width, H = Screen.height;
            FlareVisible = sp.z > 0f && sp.x > -W && sp.x < 2f * W && sp.y > -H && sp.y < 2f * H;
            SunScreen = new Vector2(sp.x, sp.y);
            if (FlareVisible)
            {
                float d = Vector2.Distance(new Vector2(sp.x, sp.y), new Vector2(W * 0.5f, H * 0.5f));
                flareIntensity = (layout.flareColor == 5 ? 80f : 64f) * (1f - d / (H * 0.5f));
            }
            float e = Mathf.Max((flareIntensity - 10f) / 64f, 0f);

            float s = layout.sunScale * sunScaleFactor;
            var sunRot = Quaternion.LookRotation(sun.dir, cam.transform.up);
            if (!layout.supernovaSun)
            {
                Place(sun, c, sunRot, new Vector3(s + e, s + e, 1f));
                // renderSunStreak: the scaled sun matrix scaled again by (e * (s + e + 1), s / ((1 - e) * 6 + 6)).
                Place(streak, c, sunRot, new Vector3((s + e) * e * (s + e + 1f), (s + e) * s / ((1f - e) * 6f + 6f), 1f));
                streak.t.gameObject.SetActive(e > 0f);
            }
            else
            {
                // StarSystem::render with +0xc: the sun (or the explosion ring) looks at the camera with up = game (1, 0, 0)
                // at its own scale; the streak layer at v = 0.3 x that scale with the camera's up, (v + e, v + e) as the
                // glow, then scaled again by (e (v + e + 1), v / ((1 - e) 6 + 6)) as the streak.
                var fixedRot = Mathf.Abs(Vector3.Dot(sun.dir, Vector3.right)) < 0.999f ? Quaternion.LookRotation(sun.dir, Vector3.right) : sunRot;
                float baseScale = supernova.Count > 0 ? supernovaRing : s;
                float v = 0.3f * baseScale;
                var glowScale = new Vector3(v + e, v + e, v);
                var streakScale = Vector3.Scale(glowScale, new Vector3(e * (v + e + 1f), v / ((1f - e) * 6f + 6f), v));
                if (supernova.Count == 0)
                {
                    Place(sun, c, fixedRot, new Vector3(s, s, 1f));
                    Place(glow, c, sunRot, new Vector3(glowScale.x, glowScale.y, 1f));
                    Place(streak, c, sunRot, new Vector3(streakScale.x, streakScale.y, 1f));
                    streak.t.gameObject.SetActive(e > 0f);
                }
                for (int i = 0; i < supernova.Count; i++)
                {
                    var (part, norm, kind) = supernova[i];
                    if (part == null) continue;
                    var scale = kind == NovaPart.Ring ? Vector3.one * baseScale : kind == NovaPart.CoreGlow ? glowScale : streakScale;
                    part.SetPositionAndRotation(c + sun.dir * Distance, kind == NovaPart.Ring ? fixedRot : sunRot);
                    part.localScale = scale * norm;
                    if (kind == NovaPart.CoreStreak) part.gameObject.SetActive(e > 0f);
                }
            }

            // getPlanetScaleFactor: f = clamp(camera game z / -800000, -0.2, 0.2) (game z = -Unity z / 0.05).
            float gameZ = -c.z / OrbitLayout.MetersPerUnit;
            float zoom = Mathf.Clamp(gameZ / -800000f, -0.2f, 0.2f);
            foreach (var p in planets)
            {
                // StarSystem::render: not in the alien orbit or a planet ring orbit (the orbit planet stays at its size).
                float k = p.orbitPlanet && !layout.ringOrbit && !layout.alienOrbit ? p.scale + zoom : p.scale;
                Place(p, c, p.rot, Vector3.one * k);
            }
            foreach (var r in rings) Place(r, c, r.rot, Vector3.one * r.scale);
        }

        static void Place(Body b, Vector3 camPos, Quaternion rot, Vector3 scale)
        {
            b.t.SetPositionAndRotation(camPos + b.dir * Distance, rot);
            b.t.localScale = scale * QuadMeters;
        }
    }
}
