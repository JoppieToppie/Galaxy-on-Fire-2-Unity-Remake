// OrbitStorm.cs
// Remake (2026-11-06, for mods): an orbit's lightning storm (stations.json "orbit.storm", Modding.ModWorld.StormSpec), after
// EVERSPACE 2's BP_LightningField (AttachToCamera, Frequency 1, Radius 400 000 cm, HeightPercent 1, VolumeSurfaceOnly,
// ThunderSoundSpawnChance 0.2) and its P_LightningField: flashes on the surface of a cylinder round the camera, 'frequency'
// a second (at random), each a glow (M_Particle_Glow_Unlit_001: 1-10 km across for 0.1-0.2 s) and an arc
// (MI_Particle_Lightning_Arc: 1-3 x 2 km for 0.25-1 s), both going from blue-violet (0.1, 0.34, 1) to orange
// (1, 0.29, 0.05) over their life as its ColorOverLife does, very bright (HDR, the bloom picks them up). Drawn with GoF2's
// own sprites: the particle atlas's glow and the EMP lightning's big bolt (khador_jump.png, EmpSparks). A thunder (one of
// the mod's sounds) follows a flash at 'thunderChance', a little later.

using System.Collections.Generic;
using GoF2Remake.Data;
using GoF2Remake.Flight;
using UnityEngine;

namespace GoF2Remake.World
{
    public class OrbitStorm : MonoBehaviour
    {
        const int GlowCell = 3, BoltTilesX = 4, BoltTilesY = 4, BoltTile = 11;
        public static readonly Color Early = new Color(0.1f, 0.339f, 1f), Late = new Color(1f, 0.2875f, 0.05f);

        Modding.ModWorld.StormSpec spec;
        ParticleSystem glows, bolts;
        AudioSource thunder;
        float nextMs, thunderMs = -1f;

        public static OrbitStorm Attach(SpaceLevel level)
        {
            var spec = level != null && level.Layout != null && !level.Layout.interstellar ? Modding.ModWorld.OrbitOf(level.Layout.stationIndex)?.storm : null;
            if (spec == null) return null;
            var go = new GameObject("Orbit storm");
            go.transform.SetParent(level.transform, false);
            var s = go.AddComponent<OrbitStorm>();
            s.spec = spec;
            var assets = CombatAssets.Load();
            s.glows = s.Make("Storm glows", assets != null ? assets.particlesMaterial : null, 8, true);
            s.bolts = s.Make("Storm bolts", assets != null ? assets.empSparkMaterial : null, 8, false);
            s.thunder = go.AddComponent<AudioSource>();
            s.thunder.playOnAwake = false;
            s.thunder.spatialBlend = 0f;
            s.nextMs = NextFlashMs(spec.frequency);
            return s;
        }

        /// <summary>The wait for the next flash at 'frequency' a second (exponential: a storm's random strikes).</summary>
        public static float NextFlashMs(float frequency) => -Mathf.Log(Mathf.Max(1e-4f, 1f - Random.value)) / Mathf.Max(0.05f, frequency) * 1000f;

        /// <summary>A flash's place relative to the camera: on a cylinder of 'radius' (metres) about the world up, within
        /// +-'height' / 2 of the camera's height.</summary>
        public static Vector3 FlashOffset(float radius, float height, float angle01, float height01)
        {
            float a = angle01 * Mathf.PI * 2f;
            return new Vector3(Mathf.Cos(a) * radius, (height01 - 0.5f) * height, Mathf.Sin(a) * radius);
        }

        ParticleSystem Make(string name, Material material, int max, bool glow)
        {
            var go = new GameObject(name);
            go.transform.SetParent(transform, false);
            var ps = go.AddComponent<ParticleSystem>();
            ps.Stop(true, ParticleSystemStopBehavior.StopEmittingAndClear);
            var main = ps.main;
            main.loop = false;
            main.playOnAwake = false;
            main.startSpeed = 0f;
            main.maxParticles = max;
            main.simulationSpace = ParticleSystemSimulationSpace.World;
            main.cullingMode = ParticleSystemCullingMode.AlwaysSimulate;
            var em = ps.emission;
            em.enabled = false;
            var sh = ps.shape;
            sh.enabled = false;
            // ES2's ColorOverLife: blue-violet to orange, x the brightness; additive, so the RGB fading out at the end is the fade
            var col = ps.colorOverLifetime;
            col.enabled = true;
            float k = Mathf.Max(0f, spec.intensity) * (glow ? 1.2f : 2.5f);
            var g = new Gradient();
            g.SetKeys(new[] { new GradientColorKey(Early * k, 0f), new GradientColorKey(Late * k, 0.7f), new GradientColorKey(Color.black, 1f) },
                      new[] { new GradientAlphaKey(1f, 0f), new GradientAlphaKey(1f, 1f) });
            col.color = g;
            var r = go.GetComponent<ParticleSystemRenderer>();
            if (glow) { r.renderMode = ParticleSystemRenderMode.Mesh; r.mesh = ShipExhaust.CellQuad(GlowCell); r.alignment = ParticleSystemRenderSpace.View; }
            else
            {
                r.renderMode = ParticleSystemRenderMode.Billboard;
                r.alignment = ParticleSystemRenderSpace.View;
                r.flip = new Vector3(0.5f, 0.5f, 0f);
                var tsa = ps.textureSheetAnimation;
                tsa.enabled = true;
                tsa.mode = ParticleSystemAnimationMode.Grid;
                tsa.numTilesX = BoltTilesX;
                tsa.numTilesY = BoltTilesY;
                tsa.animation = ParticleSystemAnimationType.WholeSheet;
                tsa.frameOverTime = new ParticleSystem.MinMaxCurve((BoltTile + 0.5f) / (BoltTilesX * BoltTilesY));   // the big bolt
                tsa.cycleCount = 1;
            }
            r.maxParticleSize = 10f;
            r.shadowCastingMode = UnityEngine.Rendering.ShadowCastingMode.Off;
            r.receiveShadows = false;
            if (material != null) r.sharedMaterial = material;
            ps.Play();
            return ps;
        }

        void Update()
        {
            var cam = Camera.main;
            if (cam == null || spec == null) return;
            float dtMs = Time.deltaTime * 1000f;
            if (thunderMs >= 0f && (thunderMs -= dtMs) < 0f) PlayThunder();
            if ((nextMs -= dtMs) > 0f) return;
            nextMs = NextFlashMs(spec.frequency);
            float rM = Mathf.Min(spec.radius * OrbitLayout.MetersPerUnit, cam.farClipPlane * 0.8f);   // (in front of the far plane)
            var at = cam.transform.position + FlashOffset(rM, rM * spec.heightPercent, Random.value, Random.value);
            float scale = spec.scale;
            glows.Emit(new ParticleSystem.EmitParams
            {
                position = at, startLifetime = Random.Range(0.1f, 0.2f), startSize = Random.Range(1000f, 10000f) * scale,
                rotation = Random.Range(0f, 360f), applyShapeToPosition = false,
            }, 1);
            bolts.Emit(new ParticleSystem.EmitParams
            {
                position = at, startLifetime = Random.Range(0.25f, 1f), startSize = Random.Range(2000f, 6000f) * scale,
                rotation = Random.Range(0f, 360f), applyShapeToPosition = false,
            }, 1);
            if (spec.thunder != null && spec.thunder.Count > 0 && Random.value < spec.thunderChance && thunderMs < 0f)
                thunderMs = Random.Range(400f, 2000f);   // (the thunder after the flash)
        }

        void PlayThunder()
        {
            thunderMs = -1f;
            var clip = Modding.ModSounds.Named(spec.thunder[Random.Range(0, spec.thunder.Count)]);
            if (clip != null) thunder.PlayOneShot(clip, 0.8f * Settings.SfxVolume);
        }
    }
}
