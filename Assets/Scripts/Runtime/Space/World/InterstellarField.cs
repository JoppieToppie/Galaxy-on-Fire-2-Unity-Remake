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
        /// <summary>How fast the system's sky may turn with the way to it (degrees per ms): a change of course, not a jump.</summary>
        const float SkyTurnPerMs = 0.006f;
        /// <summary>A sky more than this far in when the flight turns to another system is kept and left from; less fades back.</summary>
        const float KeepShare = 0.5f;

        struct Sun { public Navigation.Target target; public Transform quad; }

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
            foreach (var t in nav.Targets) if (t.kind == Navigation.Kind.System && t.system == origin) originAt = t.Position;
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
                float left = s.target == blended ? 1f - SystemApproach.SunShown(approachBlend) : 1f - SystemApproach.HandedOver(approachBlend);
                left *= level.HudShown;   // none of them in the hole the ship came through: they come in after it, with the HUD
                s.quad.localScale = Vector3.one * (at * left * Mathf.Clamp(SunRadiusMeters / distance, MinAngle, MaxAngle));
            }
            UpdateSky(Time.deltaTime * 1000f);
        }

        void UpdateSky(float dtMs)
        {
            if (ship == null) return;
            var want = NextSystem();
            float goal = 0f, near = 0f;
            if (want != null)
            {
                float units = (want.Position - ship.position).magnitude / M;
                // A system the autopilot only flies past shows its sky; its orbit comes only where the ship can arrive.
                if (nav.AutopilotTarget == null || want == nav.AutopilotTarget)
                    near = 1f - Mathf.SmoothStep(0f, 1f, Mathf.InverseLerp(InterstellarSpace.ArriveUnits, InterstellarSpace.ApproachUnits, units));
                goal = Mathf.Max(near, InterstellarSpace.SkyShare((originAt - ship.position).magnitude / M, units));
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
            if (blended == null) return;
            var way = blended.Position - ship.position;
            if (way.sqrMagnitude < 1f) return;
            var turn = arrivalFacing * Quaternion.Inverse(Quaternion.LookRotation(way.normalized, Vector3.up));
            if (blend <= 0.001f) skyTurn = turn;
            else if (nav.AutopilotTarget == blended || Vector3.Dot(way, ship.forward) > 0f) skyTurn = Quaternion.RotateTowards(skyTurn, turn, SkyTurnPerMs * dtMs);
            Shader.SetGlobalMatrix("_BlendRotation", blendedSky * Matrix4x4.Rotate(skyTurn));
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
            var pilot = nav.AutopilotTarget;
            var aim = pilot != null && pilot.kind == Navigation.Kind.System && (pilot == nav.VoidDive || pilot.system != origin) ? pilot : null;
            if (aim == null) return nearest;
            float close = InterstellarSpace.ApproachUnits * M;
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
            approach?.Remove();
            foreach (var sky in skies.Values) if (sky != null && sky != RenderSettings.skybox) Destroy(sky);
        }
    }
}
