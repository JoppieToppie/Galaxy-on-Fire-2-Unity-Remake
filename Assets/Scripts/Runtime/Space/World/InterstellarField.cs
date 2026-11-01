// InterstellarField.cs
// Remake: the suns of the space between the systems (Data.InterstellarSpace, added by SpaceLevel): each system on the star
// map as its sun, the map's own sun material on a quad kept inside the far plane, a beacon from afar growing as the ship
// closes in. The warp itself is the sky's (Visuals.WarpSky). Nearing a system its own sky blends in over the one the ship left
// (GoF2/SpaceSky _SkyBlend), the sky its orbit opens with, and the rest of the orbit comes with it (SystemApproach): what the
// autopilot flies to, else the nearest. A change of target fades the last one out first. It is turned so that the way to the
// system is the way the ship will face in the orbit, so the sky ahead is the same one on both sides of the load.

using System.Collections.Generic;
using GoF2Remake.Data;
using GoF2Remake.Flight;
using GoF2Remake.UI;
using UnityEngine;

namespace GoF2Remake.World
{
    public class InterstellarField : MonoBehaviour
    {
        /// <summary>A sun's size in the space, and how small and large it may look (half its angle, radians).</summary>
        const float SunRadiusMeters = 3000f, MinAngle = 0.05f, MaxAngle = 0.35f;

        /// <summary>How fast a system's sky may change (per ms).</summary>
        const float BlendPerMs = 1f / 1500f, M = 0.05f;

        struct Sun { public Navigation.Target target; public Transform quad; }

        readonly List<Sun> suns = new List<Sun>();
        readonly Dictionary<int, Material> skies = new Dictionary<int, Material>();
        SpaceLevel level;
        Navigation nav;
        Transform ship;
        Database db;
        int origin;
        Navigation.Target blended;
        float blend;
        OrbitLayout blendedLayout;
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
            origin = level.Layout.systemIndex;
            var materials = StarMapAssets.Load()?.sunMaterials;
            var mesh = Quad();
            foreach (var t in level.Navigation.Targets)
            {
                if (t.kind != Navigation.Kind.System) continue;
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
                float left = s.target == blended ? 1f - SystemApproach.SunShown(blend) : 1f - SystemApproach.HandedOver(blend);
                s.quad.localScale = Vector3.one * (at * left * Mathf.Clamp(SunRadiusMeters / distance, MinAngle, MaxAngle));
            }
            UpdateSky(Time.deltaTime * 1000f);
        }

        void UpdateSky(float dtMs)
        {
            if (ship == null) return;
            var want = nav.AutopilotTarget != null && nav.AutopilotTarget.kind == Navigation.Kind.System ? nav.AutopilotTarget : Nearest();
            float goal = 0f;
            if (want != null && want.system != origin)
            {
                float units = (want.Position - ship.position).magnitude / M;
                goal = 1f - Mathf.SmoothStep(0f, 1f, Mathf.InverseLerp(InterstellarSpace.ArriveUnits, InterstellarSpace.ApproachUnits, units));
            }
            if (want != blended)
            {
                if (blend > 0.001f) goal = 0f;   // the last one out first
                else if (want != null && want.system != origin) { Load(want); blended = want; }
            }
            blend = Mathf.MoveTowards(blend, goal, BlendPerMs * dtMs);
            Shader.SetGlobalFloat("_SkyBlend", blended != null ? blend : 0f);
            if (blend <= 0.001f && goal <= 0f) { approach?.Remove(); approach = null; }
            if (blended == null) return;
            var way = blended.Position - ship.position;
            if (way.sqrMagnitude < 1f) return;
            var turn = arrivalFacing * Quaternion.Inverse(Quaternion.LookRotation(way.normalized, Vector3.up));
            Shader.SetGlobalMatrix("_BlendRotation", blendedSky * Matrix4x4.Rotate(turn));
            if (approach == null && goal > 0f && want == blended) approach = new SystemApproach(level, blendedLayout, arrivalAt, arrivalFacing, transform);
            approach?.Update(ship.position, way, turn, blend);
        }

        Navigation.Target Nearest()
        {
            Navigation.Target best = null;
            float nearest = float.MaxValue;
            foreach (var s in suns)
            {
                if (s.target.hidden) continue;
                float d = (s.target.Position - ship.position).sqrMagnitude;
                if (d < nearest) { nearest = d; best = s.target; }
            }
            return best;
        }

        /// <summary>The system's sky (as its arrival orbit builds it) into the shader's second set of layers.</summary>
        void Load(Navigation.Target system)
        {
            var layout = blendedLayout = OrbitLayout.Build(db, system.station);
            layout.ArrivalPose(Session.StationIndex, out arrivalAt, out arrivalFacing);
            if (!skies.TryGetValue(system.system, out var sky))
                skies[system.system] = sky = OrbitBuilder.MakeSky(layout);
            if (sky == null) return;
            blendedSky = sky.GetMatrix("_SkyRotation");
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
            approach?.Remove();
            foreach (var sky in skies.Values) if (sky != null) Destroy(sky);
        }
    }
}
