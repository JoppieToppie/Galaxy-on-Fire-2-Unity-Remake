// ModDeathFx.cs
// Remake mods (2026-11-11, after Chorus's explosions: its death effect configs' Cascade particle systems, P_Explosion_Main /
// _Sub / _Small_Sparks): a ship's own death effects (ships.json "deathFx": a JSON file in the mod, CustomFxFile) in place of
// the game's explosion. "main" plays where it explodes (NpcShipDamage.UpdateDying, PlayerHealth.UpdateDeath), "dying"
// "dyingCount" times over an NPC's death tumble at points about its hull (NpcShipDamage). Each emitter one Unity particle
// system: its image on the mods' additive glow material (ModMaterials, the colour's brightness as its HDR emission, the
// particles' colours over life its fade), bursts and a rate, life, size and size over life, a flipbook's frames, speed
// outward from a sphere (or along the effect's forward), Unreal's drag (Limit Velocity's drag), the ship's motion
// inherited, a turn and spin, stretched along its flight. Built at each spawn, gone when the last particle is; the game's
// explosion sound plays with "main". A look only: no damage (the game's explosions do none either), nothing other players
// see (their copies explode the game's way). "blend": "alpha" emitters (smoke, debris) are drawn over what is behind on the
// weapon smoke's material (ModAssets.fxAlpha), behind the added fire; "light" emitters are point lights (FxFlash).

using System.Collections.Generic;
using GoF2Remake.Data;
using GoF2Remake.Flight;
using UnityEngine;

namespace GoF2Remake.World
{
    [Unity.Scripting.LifecycleManagement.NoAutoStaticsCleanup]
    public static class ModDeathFx
    {
        const float M = 0.05f;
        static int revision = -1;
        static readonly Dictionary<int, (Modding.ModInfo mod, CustomFxFile fx, string path)> byShip = new Dictionary<int, (Modding.ModInfo, CustomFxFile, string)>();
        static readonly Dictionary<string, Material> materials = new Dictionary<string, Material>();

        [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.SubsystemRegistration)]
        static void ResetStatics() { revision = -1; byShip.Clear(); materials.Clear(); }

        /// <summary>Ship 'ship''s death effects (its mod's "deathFx" file, read once per set of mods); null = the game's.</summary>
        public static CustomFxFile Of(int ship) => Find(ship, out _);

        static CustomFxFile Find(int ship, out Modding.ModInfo mod)
        {
            if (revision != Modding.ModManager.Revision)
            {
                revision = Modding.ModManager.Revision;
                byShip.Clear(); materials.Clear();
                var files = new Dictionary<string, CustomFxFile>();
                foreach (var (m, s) in Modding.ModContent.ActiveShips())
                {
                    if (s == null || string.IsNullOrEmpty(s.deathFx)) continue;
                    string key = m.Id + "|" + s.deathFx;
                    if (!files.TryGetValue(key, out var f))
                    {
                        try
                        {
                            string text = m.Source.ReadText(s.deathFx);
                            f = string.IsNullOrEmpty(text) ? null : JsonUtility.FromJson<CustomFxFile>(text);
                            if (f == null) Warn(m, $"{Modding.ModContent.ShipsFile}: the death effects \"{s.deathFx}\" aren't in the mod (the game's explosion is used)");
                        }
                        catch (System.Exception e) { Warn(m, $"{s.deathFx}: {e.Message}"); f = null; }
                        files[key] = f;
                    }
                    if (f != null) byShip[s.index] = (m, f, s.deathFx);
                }
            }
            if (byShip.TryGetValue(ship, out var hit)) { mod = hit.mod; return hit.fx; }
            mod = null;
            return null;
        }

        /// <summary>How many "dying" effects an NPC's death tumble shows (0 = none).</summary>
        public static int DyingCount(int ship)
        {
            var f = Find(ship, out _);
            if (f?.dying?.emitters == null || f.dying.emitters.Length == 0) return 0;
            var c = f.dyingCount;
            if (c == null || c.Length == 0) return 3;
            return c.Length == 1 ? c[0] : Random.Range(Mathf.Min(c[0], c[1]), Mathf.Max(c[0], c[1]) + 1);
        }

        /// <summary>The ship's "main" death effect at 'at' facing 'rotation', the ship moving at 'velocity' (Unity / s), with the
        /// game's explosion sound; false when it has none (the caller spawns the game's explosion).</summary>
        public static bool SpawnMain(int ship, Vector3 at, Quaternion rotation, Vector3 velocity, float scale = 1f)
        {
            var f = Find(ship, out var mod);
            if (f?.main?.emitters == null || f.main.emitters.Length == 0) return false;
            Play(mod, f.main, at, rotation, velocity, scale);
            var assets = CombatAssets.Load();
            if (assets != null) Sfx.PlayAt(Random.value < 0.5f ? CombatAssets.Pick(assets.explosionBig) : CombatAssets.Pick(assets.explosionMid), at);
            return true;
        }

        /// <summary>One of the ship's "dying" effects at 'at' (a point on its hull).</summary>
        public static void SpawnDying(int ship, Vector3 at, Quaternion rotation, Vector3 velocity, float scale = 1f)
        {
            var f = Find(ship, out var mod);
            if (f?.dying?.emitters == null) return;
            Play(mod, f.dying, at, rotation, velocity, scale);
        }

        /// <summary>Plays 'fx' at 'at' (the root is gone once its last particle is). -> the root.</summary>
        public static GameObject Play(Modding.ModInfo mod, CustomFx fx, Vector3 at, Quaternion rotation, Vector3 velocity, float scale = 1f)
        {
            var root = new GameObject("Death effect");
            root.transform.SetPositionAndRotation(at, rotation);
            float longest = 0f;
            foreach (var e in fx.emitters)
            {
                if (e == null) continue;
                if (e.light > 0f) { longest = Mathf.Max(longest, FxFlash.Spawn(e, root.transform, scale).Lasts); continue; }   // a light, no particles
                var ps = Build(mod, e, root.transform, velocity, scale, out float lasts);
                if (ps == null) continue;
                longest = Mathf.Max(longest, lasts);
                ps.Play(true);
            }
            if (Application.isPlaying) Object.Destroy(root, longest + 0.5f);
            return root;
        }

        static float Min(float[] v, float d) => v != null && v.Length > 0 ? v[0] : d;
        static float Max(float[] v, float d) => v != null && v.Length > 1 ? v[1] : Min(v, d);

        /// <summary>One emitter's particle system under 'root'; 'lasts' how long it takes to play out (s).</summary>
        public static ParticleSystem Build(Modding.ModInfo mod, CustomFxEmitter e, Transform root, Vector3 velocity, float scale, out float lasts, bool asTrail = false)
        {
            lasts = 0f;
            var go = new GameObject("Emitter");
            go.transform.SetParent(root, false);
            var ps = go.AddComponent<ParticleSystem>();
            ps.Stop(true, ParticleSystemStopBehavior.StopEmittingAndClear);
            float k = M * Mathf.Max(0.01f, scale);

            int count = 0;
            float lastBurst = 0f;
            var bursts = new List<ParticleSystem.Burst>();
            if (e.bursts != null)
                for (int i = 0; i + 1 < e.bursts.Length; i += 2)
                {
                    int n = Mathf.Max(0, Mathf.RoundToInt(e.bursts[i + 1]));
                    bursts.Add(new ParticleSystem.Burst(Mathf.Max(0f, e.bursts[i]), (short)n));
                    count += n; lastBurst = Mathf.Max(lastBurst, e.bursts[i]);
                }
            float duration = Mathf.Max(0.05f, Mathf.Max(e.duration, lastBurst + 0.02f));
            count += Mathf.CeilToInt(Mathf.Max(0f, e.rate) * duration);
            if (count == 0 && !asTrail) { Object.Destroy(go); return null; }

            var main = ps.main;
            main.duration = duration;
            main.loop = false;
            main.playOnAwake = false;
            main.startLifetime = new ParticleSystem.MinMaxCurve(Mathf.Max(0.01f, Min(e.life, 1f)), Mathf.Max(0.01f, Max(e.life, 1f)));
            main.startSize = new ParticleSystem.MinMaxCurve(Min(e.size, 100f) * k, Max(e.size, 100f) * k);
            main.startSpeed = new ParticleSystem.MinMaxCurve(Min(e.speed, 0f) * k, Max(e.speed, 0f) * k);
            main.startColor = Color.white;
            main.startRotation = e.randomRotation ? new ParticleSystem.MinMaxCurve(0f, 2f * Mathf.PI) : new ParticleSystem.MinMaxCurve(0f);
            main.simulationSpace = ParticleSystemSimulationSpace.World;
            main.emitterVelocityMode = ParticleSystemEmitterVelocityMode.Custom;
            main.emitterVelocity = velocity;
            main.maxParticles = Mathf.Min(count + 8, 2000);
            main.scalingMode = ParticleSystemScalingMode.Hierarchy;
            lasts = duration + Max(e.life, 1f);

            var em = ps.emission;
            em.rateOverTime = Mathf.Max(0f, e.rate);
            em.SetBursts(bursts.ToArray());

            var sh = ps.shape;
            sh.enabled = true;
            if (e.forward)
            {
                // along the effect's forward from a disc of the sphere's radius
                sh.shapeType = ParticleSystemShapeType.Cone;
                sh.angle = 0f;
                sh.radius = Mathf.Max(0.0001f, e.sphere * k);
            }
            else
            {
                sh.shapeType = ParticleSystemShapeType.Sphere;
                sh.radius = Mathf.Max(0.0001f, e.sphere * k);
            }

            if (e.inherit > 0f)
            {
                var iv = ps.inheritVelocity;
                iv.enabled = true;
                iv.mode = ParticleSystemInheritVelocityMode.Initial;
                iv.curve = new ParticleSystem.MinMaxCurve(e.inherit);
            }
            if (e.drag > 0f)
            {
                var lv = ps.limitVelocityOverLifetime;
                lv.enabled = true;
                lv.limit = new ParticleSystem.MinMaxCurve(1e6f);
                lv.drag = new ParticleSystem.MinMaxCurve(e.drag);
                lv.multiplyDragByParticleSize = false;
                lv.multiplyDragByParticleVelocity = false;
            }
            if (e.spin != null && e.spin.Length > 0)
            {
                var rl = ps.rotationOverLifetime;
                rl.enabled = true;
                rl.z = new ParticleSystem.MinMaxCurve(Min(e.spin, 0f) * Mathf.Deg2Rad, Max(e.spin, 0f) * Mathf.Deg2Rad);
            }
            if (e.sizeOverLife != null && e.sizeOverLife.Length > 1)
            {
                float top = 1e-6f;
                foreach (var v in e.sizeOverLife) top = Mathf.Max(top, v);
                var sol = ps.sizeOverLifetime;
                sol.enabled = true;
                sol.size = new ParticleSystem.MinMaxCurve(top, Samples(e.sizeOverLife, 1f / top));
            }
            var col = ps.colorOverLifetime;
            col.enabled = true;
            col.color = new ParticleSystem.MinMaxGradient(Fade(e.colorOverLife, e.alphaOverLife));

            int cols = e.sheet != null && e.sheet.Length > 0 ? Mathf.Max(1, e.sheet[0]) : 1, rows = e.sheet != null && e.sheet.Length > 1 ? Mathf.Max(1, e.sheet[1]) : 1;
            if (cols * rows > 1)
            {
                var ts = ps.textureSheetAnimation;
                ts.enabled = true;
                ts.numTilesX = cols; ts.numTilesY = rows;
                ts.animation = ParticleSystemAnimationType.WholeSheet;
                if (e.randomFrame || e.frames == null || e.frames.Length < 2)
                {
                    ts.frameOverTime = new ParticleSystem.MinMaxCurve(0f);
                    ts.startFrame = e.randomFrame ? new ParticleSystem.MinMaxCurve(0f, cols * rows - 1) : new ParticleSystem.MinMaxCurve(0f);
                }
                else ts.frameOverTime = new ParticleSystem.MinMaxCurve(1f, Samples(e.frames, 1f));
            }

            var r = go.GetComponent<ParticleSystemRenderer>();
            r.sharedMaterial = MaterialFor(mod, e);
            if (e.stretch > 0f)
            {
                r.renderMode = ParticleSystemRenderMode.Stretch;
                r.lengthScale = e.stretch;
                r.velocityScale = 0f;
            }
            r.shadowCastingMode = UnityEngine.Rendering.ShadowCastingMode.Off;
            r.receiveShadows = false;
            if (Alpha(e)) r.sortingFudge = 10f;   // (smoke and debris behind the fire added over them)
            if (e.noise != null && e.noise.Length > 0 && e.noise[0] > 0f)
            {
                // Chorus's local vector field: a turbulence (its intensity as the noise's strength, its cell as the frequency)
                var nz = ps.noise;
                nz.enabled = true;
                nz.strength = new ParticleSystem.MinMaxCurve(e.noise[0] * k);
                nz.frequency = 1f / Mathf.Max(0.01f, (e.noise.Length > 1 ? e.noise[1] : 1000f) * k);
                nz.scrollSpeed = 0.3f;
                nz.damping = true;
            }
            if (e.trail != null && !string.IsNullOrEmpty(e.trail.image) && e.trail.every > 0f)   // (JsonUtility makes an empty one when absent)
            {
                // Chorus's SpawnPerUnit off another emitter's particles: a child system born with each particle, leaving puffs
                // along its flight
                var t = e.trail;
                var te = new CustomFxEmitter
                {
                    image = t.image, blend = t.blend, sheet = t.sheet, frames = t.frames, randomFrame = t.randomFrame,
                    randomRotation = t.randomRotation, life = t.life, size = t.size, sizeOverLife = t.sizeOverLife, color = t.color,
                    colorOverLife = t.colorOverLife, alphaOverLife = t.alphaOverLife, rate = 0f, bursts = new float[] { 0f, 0f },
                    duration = Max(e.life, 1f), inherit = 0f,
                };
                var child = Build(mod, te, go.transform, Vector3.zero, scale, out float childLasts, true);
                if (child != null)
                {
                    var cem = child.emission;
                    cem.rateOverDistance = 1f / Mathf.Max(0.01f, t.every * k);
                    var cm = child.main;
                    cm.maxParticles = 1000;
                    var sub = ps.subEmitters;
                    sub.enabled = true;
                    sub.AddSubEmitter(child, ParticleSystemSubEmitterType.Birth, ParticleSystemSubEmitterProperties.InheritNothing);
                    lasts = Mathf.Max(lasts, duration + Max(e.life, 1f) + childLasts);
                }
                if (string.IsNullOrEmpty(e.image)) r.renderMode = ParticleSystemRenderMode.None;   // (only the trails' source)
            }
            return ps;
        }

        /// <summary>A curve through 'samples' spread evenly over 0..1, times 'k'.</summary>
        static AnimationCurve Samples(float[] samples, float k)
        {
            var c = new AnimationCurve();
            int n = samples.Length;
            for (int i = 0; i < n; i++) c.AddKey(n > 1 ? i / (float)(n - 1) : 0f, samples[i] * k);
            return c;
        }

        /// <summary>The particles' colour over life: 'rgb' [r, g, b, ...] and 'alpha' samples 0..1 (at most 8 keys each).</summary>
        static Gradient Fade(float[] rgb, float[] alpha)
        {
            var g = new Gradient();
            var ck = new List<GradientColorKey>();
            if (rgb != null && rgb.Length >= 3)
            {
                int n = rgb.Length / 3;
                for (int j = 0; j < Mathf.Min(n, 8); j++)
                {
                    int i = n <= 8 ? j : Mathf.RoundToInt(j * (n - 1) / 7f);
                    ck.Add(new GradientColorKey(new Color(rgb[i * 3], rgb[i * 3 + 1], rgb[i * 3 + 2]), n > 1 ? i / (float)(n - 1) : 0f));
                }
            }
            if (ck.Count == 0) ck.Add(new GradientColorKey(Color.white, 0f));
            var ak = new List<GradientAlphaKey>();
            if (alpha != null && alpha.Length > 0)
            {
                int n = alpha.Length;
                for (int j = 0; j < Mathf.Min(n, 8); j++)
                {
                    int i = n <= 8 ? j : Mathf.RoundToInt(j * (n - 1) / 7f);
                    ak.Add(new GradientAlphaKey(Mathf.Clamp01(alpha[i]), n > 1 ? i / (float)(n - 1) : 0f));
                }
            }
            if (ak.Count == 0) ak.Add(new GradientAlphaKey(1f, 0f));
            g.SetKeys(ck.ToArray(), ak.ToArray());
            return g;
        }

        /// <summary>The emitter's material: the mods' additive glow with its image, its colour as the HDR emission (shared by
        /// image and colour).</summary>
        static Material MaterialFor(Modding.ModInfo mod, CustomFxEmitter e)
        {
            var c = e.color != null && e.color.Length >= 3 ? new Color(e.color[0], e.color[1], e.color[2]) : Color.white;
            float peak = Mathf.Max(c.r, Mathf.Max(c.g, c.b));
            bool alpha = Alpha(e);
            string key = (mod != null ? mod.Id : "") + "|" + e.image + "|" + c + (alpha ? "|alpha" : "");
            if (materials.TryGetValue(key, out var m) && m != null) return m;
            if (alpha)
            {
                // "blend": "alpha": the weapon smoke's template (GoF2/AlphaBlend: the image x the tint x the particles' colour)
                var template = Modding.ModAssets.Get()?.fxAlpha;
                if (template != null)
                {
                    m = new Material(template) { name = "Death effect " + e.image };
                    var tex = mod != null && !string.IsNullOrEmpty(e.image) ? Modding.ModMaterials.Texture(mod, e.image, false) : null;
                    if (tex != null) m.SetTexture("_MainTex", tex);
                    m.SetColor("_Color", new Color(c.r, c.g, c.b, 1f));
                    materials[key] = m;
                    return m;
                }
            }
            var spec = new CustomShipMaterial
            {
                material = "fx", additive = true, emission = e.image,
                emissionColor = peak > 1e-6f ? new[] { c.r / peak, c.g / peak, c.b / peak } : new[] { 1f, 1f, 1f },
                emissionIntensity = Mathf.Max(1e-3f, peak),
            };
            m = mod != null ? Modding.ModMaterials.FromSpec(mod, spec, "Death effect " + e.image) : null;
            if (m == null) m = CombatAssets.Load()?.explosionSpriteMaterial;
            materials[key] = m;
            return m;
        }

        /// <summary>The emitter draws its image alpha-blended ("blend": "alpha"), not added.</summary>
        public static bool Alpha(CustomFxEmitter e) => e != null && string.Equals(e.blend, "alpha", System.StringComparison.OrdinalIgnoreCase);

        static void Warn(Modding.ModInfo mod, string message)
        {
            if (!mod.Warnings.Contains(message)) mod.Warnings.Add(message);
            Debug.LogWarning($"Mods: {mod.Id}: {message}");
        }
    }
}
