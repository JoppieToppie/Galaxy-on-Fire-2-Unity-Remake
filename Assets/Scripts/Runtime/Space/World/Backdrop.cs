// Backdrop.cs
// Sun, planets, planet rings and the sun streak of the current orbit (StarSystem::render 0x15db84,
// renderSunStreak). They are quads on the original's `plane` (65000 units wide) kept 20000 units (1000 m) from the
// camera every frame, so they stay at infinity; the GoF2/Backdrop shader draws them behind all geometry.
//   Sun: additive, faces the camera with the camera's up, swells by e = max((I - 10) / 64, 0) where I is the lens
//        flare intensity 64 * (1 - d / (H / 2)) (d = sun distance from the screen centre in pixels).
//   Streak: the sun texture stretched horizontally by e (invisible when the sun is off-centre).
//   The supernova system (StarSystem+0xc, OrbitLayout.supernovaSun): the sun faces the camera with game +x as its up (no
//        roll with the camera; also in Tadram's / Naneroh's orbits, whose sun lies 0.02 deg off game x) and never swells; the streak layer (sn_sun_011) is drawn twice: right after the sun as a
//        glow of 0.3 s + e facing the camera with the camera's up, then as the streak (that glow scaled again). After the
//        explosion the ring takes the sun's part and the core mesh the streak layer's (both drawings).
//   Planets: fixed orientation (not billboards), alpha blended, mirrored so the lit rim faces the sun; the orbit
//        planet (straight ahead, Unity +Z) grows up to +0.2 scale as the camera flies toward it.
//   Rings: the planet's quad x4 with sn_planet_ring.
//   Remake mods: a planet whose texture a mod replaces is drawn as a star when a mod's backdrop.json asks for it
//        (ModBackdrop.StarPlanets): it faces the camera with the camera's up like the sun, isn't mirrored, has no ring, and
//        its near-white core is lifted to planetGlow under the remake's bloom (a small glow); looked at, it gets the sun's
//        flare at planetFlare of its strength: a slight swell and a horizontal streak (its own texture, additive).
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
            public MeshRenderer r;       // t's renderer (the streak's is updated every frame)
            public Vector3 dir, dir0;    // Unity direction from the camera, as now and as the orbit has it
            public Quaternion rot, rot0; // fixed orientation (planets)
            public float scale;
            public bool orbitPlanet;
            public bool star;            // remake mods: drawn as a star (faces the camera, its core glows)
            public Body starStreak;      // remake mods: the star's horizontal streak (additive)
        }

        // Remake mods: the star planets' streak: its length and height at full flare, in the star's own size.
        const float StarStreakLength = 8f, StarStreakHeight = 0.15f;

        /// <summary>Modified: the sun's streak a third longer than renderSunStreak's, both ways from the sun (matched by eye
        /// to the original's in a screenshot).</summary>
        const float StreakLength = 1.35f;
        /// <summary>Modified: and 25 % thicker (its height on screen).</summary>
        const float StreakThickness = 1.25f;
        readonly List<Material> ownMaterials = new List<Material>();
        void OnDestroy() { foreach (var m in ownMaterials) if (m != null) Destroy(m); }

        Camera cam;
        OrbitLayout layout;
        Body sun, streak, glow;
        readonly List<Body> planets = new List<Body>(), bodies = new List<Body>();

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
            var sunMat = Load(orbit.sunTexture, true);
            sun = Make("Sun", sunMat, 2900, sunDir, Quaternion.identity, orbit.sunScale);
            // StarSystem::renderSunStreak: the supernova system draws its streak with sn_sun_011 (StarSystem+0x10, 0x2dde).
            var streakMat = orbit.supernovaSun ? Load("sn_sun_011") ?? sunMat : sunMat;
            streak = Make("SunStreak", streakMat, 2903, sunDir, Quaternion.identity, 0f);
            // The supernova system's glow (StarSystem::render: renderSunStreak right after the sun). Additive like the sun,
            // so the shared queue doesn't matter.
            if (orbit.supernovaSun) glow = Make("SunGlow", streakMat, 2900, sunDir, Quaternion.identity, 0f);
            sunGlowStyle = -1;
            ApplySunGlow();   // after the planets (below): sets their glow too

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
                var body = Make(p.texture, Load(p.texture, false, out bool modTexture), 2901, dir, rot, p.scale);
                body.orbitPlanet = p.isOrbitPlanet;
                bool starStyle = Modding.ModBackdrop.StarPlanets(out _, out float flare);
                body.star = modTexture && starStyle;
                SetProps(body.t, mirror && !body.star, tint);
                // Remake mods: the star's streak, its own texture added (weighted by its alpha) like the sun's streak.
                var starMat = body.t.GetComponent<MeshRenderer>().sharedMaterial;
                if (body.star && flare > 0f && starMat != null)
                {
                    var starStreakMat = new Material(starMat) { name = starMat.name + " (streak)", renderQueue = 2903 };
                    starStreakMat.SetFloat("_SrcBlend", (float)BlendMode.SrcAlpha);   // the PNG's transparent texels are white: weighted by alpha
                    starStreakMat.SetFloat("_DstBlend", (float)BlendMode.One);
                    ownMaterials.Add(starStreakMat);
                    body.starStreak = Make(p.texture + "_streak", starStreakMat, 2903, dir, rot, p.scale);
                    SetProps(body.starStreak.t, false, Color.clear);
                    body.starStreak.t.gameObject.SetActive(false);
                }
                planets.Add(body);
                PlanetTargets.Add((p.station, body.t, p.isOrbitPlanet));
                if (p.ring && ringMat != null && !body.star)
                {
                    var ring = Make(p.texture + "_ring", ringMat, 2902, dir, rot, p.scale * 4f);
                    SetProps(ring.t, mirror, Color.clear);
                    rings.Add(ring);
                }
            }
            sunGlowStyle = -1;
            ApplySunGlow();   // the star planets' glow (remake mods)
        }

        // ---- the orbit seen from outside it, the ship flying in (SystemApproach)

        MaterialPropertyBlock fadeBlock;
        /// <summary>How much of the sun's swell shows, and the camera's z the orbit planet's size goes by, in place of its own.</summary>
        [System.NonSerialized] public float swell = 1f;
        [System.NonSerialized] public float? orbitZ;

        /// <summary>Everything turned by 'turn', the sun's light (with its streak) drawn along from 'sunFrom' as 'sunAt' goes to 1:
        /// it starts as the star ahead and ends where the orbit has it.</summary>
        public void Turn(Quaternion turn, Vector3 sunFrom, float sunAt)
        {
            foreach (var b in bodies)
            {
                b.dir = turn * b.dir0;
                b.rot = turn * b.rot0;
            }
            sun.dir = streak.dir = Vector3.Slerp(sunFrom, sun.dir, sunAt);
            if (glow != null) glow.dir = sun.dir;
        }

        /// <summary>The bodies' opacity: the planets and rings 'planets', the sun's layers 'sunLayers'.</summary>
        public void Fade(float planetLayers, float sunLayers)
        {
            fadeBlock ??= new MaterialPropertyBlock();
            foreach (var b in bodies)
            {
                float f = b == sun || b == streak || b == glow ? sunLayers : planetLayers;
                bool added = b.r.sharedMaterial != null && b.r.sharedMaterial.HasProperty("_DstBlend") && b.r.sharedMaterial.GetFloat("_DstBlend") == (float)BlendMode.One;
                b.r.GetPropertyBlock(fadeBlock);
                fadeBlock.SetColor("_Color", added ? new Color(f, f, f, f) : new Color(1f, 1f, 1f, f));
                b.r.SetPropertyBlock(fadeBlock);
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

        static Material Load(string texture, bool sun = false) => Load(texture, sun, out _);

        /// <summary>'modTexture': a mod's texture replaces the game's (ModTextures).</summary>
        static Material Load(string texture, bool sun, out bool modTexture)
        {
            modTexture = false;
            // Remake mods: a mod's planet / sun PNG ("mod:<id>|<path>", ModBackdrop).
            if (Modding.ModBackdrop.IsMod(texture)) return Modding.ModBackdrop.Material(texture, sun);
            var m = Resources.Load<Material>($"{MaterialFolder}/{texture}");
            if (m == null) Debug.LogWarning($"Backdrop: missing material Resources/{MaterialFolder}/{texture}");
            // Remake mods: a texture replacement named like the game's (textures/planet_000_small.png...), as on the ships.
            var replaced = Modding.ModTextures.Replace(m);
            modTexture = replaced != m;
            if (replaced != m && replaced.HasProperty("_MainTex") && replaced.GetTexture("_MainTex") is Texture t)
                t.wrapMode = TextureWrapMode.Clamp;   // a single planet / sun image: no bleeding from the opposite edge
            return replaced;
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
            var body = new Body { t = go.transform, r = r, dir = dir, dir0 = dir, rot = rot, rot0 = rot, scale = scale };
            bodies.Add(body);
            return body;
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
                // The streak (and the supernova glow) get the core's HDR lift like the sun; over the sun's disc the streak
                // still can't add past the sun's white (GoF2/Backdrop's _SunInStreak limit, applied before the lift).
                block.SetFloat("_CoreGlow", core);
                b.r.SetPropertyBlock(block);
            }
            // Remake mods: the star planets' small glow (only under the remake's bloom, like the sun's core).
            Modding.ModBackdrop.StarPlanets(out float planetGlow);
            foreach (var p in planets)
            {
                if (!p.star) continue;
                var r = p.t.GetComponent<MeshRenderer>();
                var block = new MaterialPropertyBlock();
                r.GetPropertyBlock(block);   // keeps _Mirror / _Tint
                block.SetFloat("_CoreGlow", style == Data.Settings.BloomRemake ? planetGlow : 1f);
                r.SetPropertyBlock(block);
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
            float e = NoSwell ? 0f : Mathf.Max((flareIntensity - 10f) / 64f, 0f) * swell;

            float s = layout.sunScale * sunScaleFactor;
            var sunRot = Quaternion.LookRotation(sun.dir, cam.transform.up);
            if (!layout.supernovaSun)
            {
                Place(sun, c, sunRot, new Vector3(s + e, s + e, 1f));
                // renderSunStreak: the scaled sun matrix scaled again by (e * (s + e + 1), s / ((1 - e) * 6 + 6)).
                float streakY = (s + e) * s / ((1f - e) * 6f + 6f) * StreakThickness;
                float streakX = (s + e) * e * (s + e + 1f) * StreakLength;
                Place(streak, c, sunRot, new Vector3(streakX, streakY, 1f));
                streak.t.gameObject.SetActive(e > 0f);
                SunInStreak(streak, new Vector2(s + e, s + e), new Vector2(streakX, streakY));
            }
            else
            {
                // StarSystem::render with +0xc: the sun (or the explosion ring) looks at the camera with up = game (1, 0, 0)
                // at its own scale; the streak layer at v = 0.3 x that scale with the camera's up, (v + e, v + e) as the
                // glow, then scaled again by (e (v + e + 1), v / ((1 - e) 6 + 6)) as the streak.
                var fixedRot = SupernovaSunRotation(sun.dir, sunRot);
                float baseScale = supernova.Count > 0 ? supernovaRing : s;
                float v = 0.3f * baseScale;
                var glowScale = new Vector3(v + e, v + e, v);
                var streakScale = Vector3.Scale(glowScale, new Vector3(e * (v + e + 1f) * StreakLength, v / ((1f - e) * 6f + 6f) * StreakThickness, v));
                if (supernova.Count == 0)
                {
                    Place(sun, c, fixedRot, new Vector3(s, s, 1f));
                    Place(glow, c, sunRot, new Vector3(glowScale.x, glowScale.y, 1f));
                    Place(streak, c, sunRot, new Vector3(streakScale.x, streakScale.y, 1f));
                    streak.t.gameObject.SetActive(e > 0f);
                    // Limited against the glow quad only (the streak's own texture, the same up): the sun under both has
                    // its own texture and a world-fixed roll, which _SunInStreak's axis-aligned uv mapping can't follow,
                    // so over the supernova sun's disc the streak can still add past its white.
                    SunInStreak(streak, new Vector2(glowScale.x, glowScale.y), new Vector2(streakScale.x, streakScale.y));
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
            float gameZ = -(orbitZ ?? c.z) / OrbitLayout.MetersPerUnit;
            float zoom = Mathf.Clamp(gameZ / -800000f, -0.2f, 0.2f);
            foreach (var p in planets)
            {
                // StarSystem::render: not in the alien orbit or a planet ring orbit (the orbit planet stays at its size).
                float k = p.orbitPlanet && !layout.ringOrbit && !layout.alienOrbit ? p.scale + zoom : p.scale;
                if (!p.star) { Place(p, c, p.rot, Vector3.one * k); continue; }
                // Remake mods: a star faces the camera with its roll, like the sun, and gets the sun's flare (render2D's
                // intensity from its own screen position) at planetFlare: it swells a little and streaks sideways.
                Modding.ModBackdrop.StarPlanets(out _, out float flare);
                float ef = 0f;
                var ps = cam.WorldToScreenPoint(c + p.dir * Distance);
                if (ps.z > 0f)
                {
                    float pd = Vector2.Distance(new Vector2(ps.x, ps.y), new Vector2(W * 0.5f, H * 0.5f));
                    ef = Mathf.Max((64f * (1f - pd / (H * 0.5f)) - 10f) / 64f, 0f) * flare;
                }
                var starRot = Quaternion.LookRotation(p.dir, cam.transform.up);
                float size = k * (1f + ef);
                Place(p, c, starRot, Vector3.one * size);
                if (p.starStreak != null)
                {
                    Place(p.starStreak, c, starRot, new Vector3(size * (1f + ef * StarStreakLength), size * StarStreakHeight, 1f));
                    p.starStreak.t.gameObject.SetActive(ef > 0f);
                }
            }
            foreach (var r in rings) Place(r, c, r.rot, Vector3.one * r.scale);
        }

        /// <summary>The supernova sun's world-fixed rotation: facing 'dir' with game +x (Unity +x) as its up, as
        /// MatrixGetLookAt builds it (up = normalize(cross(cross(x, f), f))). Sun slot 6 (Tadram 113, Naneroh 109) lies at
        /// yaw 0x3FFC, 0.02 deg off the x axis: the original's cross product is tiny but well defined there, so the up is
        /// x projected onto the sun's plane, in double precision (a float threshold took it as parallel and used the
        /// camera's up: the sun rolled with the view in those orbits). Only an exactly parallel direction keeps 'fallback'.</summary>
        static Quaternion SupernovaSunRotation(Vector3 dir, Quaternion fallback)
        {
            double fx = dir.x, fy = dir.y, fz = dir.z;
            double len = System.Math.Sqrt(fx * fx + fy * fy + fz * fz);
            if (len < 1e-12) return fallback;
            fx /= len; fy /= len; fz /= len;
            double ux = fy * fy + fz * fz, uy = -fx * fy, uz = -fx * fz;   // x - (x . f) f (1 - fx^2 written without the cancellation)
            double ul = System.Math.Sqrt(ux * ux + uy * uy + uz * uz);
            if (ul < 1e-9) return fallback;
            return Quaternion.LookRotation(dir, new Vector3((float)(ux / ul), (float)(uy / ul), (float)(uz / ul)));
        }

        MaterialPropertyBlock streakBlock;

        /// <summary>Modified: the sun never swells or streaks (the station's rooms: StationLevel).</summary>
        [System.NonSerialized] public bool NoSwell;

        /// <summary>Modified: where the sun's quad lies in the streak's uv (GoF2/Backdrop _SunInStreak), so the streak adds
        /// over the sun only up to white, like the original's gamma-space blending.</summary>
        void SunInStreak(Body b, Vector2 sunSize, Vector2 streakSize)
        {
            if (b == null || !b.t.gameObject.activeSelf || streakSize.x <= 0f || sunSize.y <= 0f) return;
            var r = b.r;
            streakBlock ??= new MaterialPropertyBlock();
            r.GetPropertyBlock(streakBlock);   // keeps _Color / _CoreGlow
            streakBlock.SetVector("_SunInStreak", new Vector4(sunSize.x / streakSize.x, streakSize.y / sunSize.y, 0f, 0f));
            r.SetPropertyBlock(streakBlock);
        }

        static void Place(Body b, Vector3 camPos, Quaternion rot, Vector3 scale)
        {
            b.t.SetPositionAndRotation(camPos + b.dir * Distance, rot);
            b.t.localScale = scale * QuadMeters;
        }
    }
}
