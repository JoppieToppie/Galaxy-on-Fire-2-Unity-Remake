// PhotoMotionPass.cs
// Remake (a player's request, 2026-11): motion blur for the photo tools' Shutter angle (Action Freeze's Camera settings,
// UI.PhotoTools). Action Freeze holds the world still (Time.timeScale 0), so URP's motion blur, which compares a frame
// with the one before, sees nothing move. This keeps every ship's velocity from the frames before the freeze instead
// (Track: Target.All's ships, sampled once a rendered frame while the game time runs) and blurs the frozen picture by
// how far each point would travel while the shutter is open: the ships at their velocities (a sphere around each, from
// its renderers), the rest of the world standing, the camera at the velocity it is given (the ship's in the orbit, so
// the world streaks past a sharp ship like a tracking shot; none for the free camera, so the ships streak). Translation
// only: a turning ship blurs as if it flew straight. A render-graph pass before the post-processing on the one camera the
// photo tools gave it, ahead of PhotoBlurPass. Shader: Resources/GoF2PostFx/PhotoMotion (its header: the passes).

using System.Collections.Generic;
using GoF2Remake.Flight;
using UnityEngine;
using UnityEngine.Experimental.Rendering;
using UnityEngine.Rendering;
using UnityEngine.Rendering.RenderGraphModule;
using UnityEngine.Rendering.RenderGraphModule.Util;
using UnityEngine.Rendering.Universal;

namespace GoF2Remake.Visuals
{
    [Unity.Scripting.LifecycleManagement.NoAutoStaticsCleanup]
    public sealed class PhotoMotionPass : ScriptableRenderPass
    {
        public const int MaxSpheres = 32;   // the shader's MAX_SPHERES
        const int PassVelocity = 0, PassTileMax = 1, PassNeighbour = 2, PassGather = 3;
        const float TeleportSpeed = 5000f;  // m/s: a jump or a respawn, not a flight
        static readonly int DepthTex = Shader.PropertyToID("_DepthTex"), VelocityTex = Shader.PropertyToID("_VelocityTex"),
            NeighbourTex = Shader.PropertyToID("_NeighbourTex"), Spheres = Shader.PropertyToID("_Spheres"),
            SphereVel = Shader.PropertyToID("_SphereVel"), SphereCount = Shader.PropertyToID("_SphereCount"),
            CamVel = Shader.PropertyToID("_CamVel"), ExposureId = Shader.PropertyToID("_Exposure"),
            Size = Shader.PropertyToID("_Size"), Tile = Shader.PropertyToID("_Tile"), TileStride = Shader.PropertyToID("_TileStride");

        sealed class Motion { public Vector3 position, velocity; public int frame; }
        sealed class Shape { public Vector3 localCentre; public float radius; }

        static readonly Dictionary<Target, Motion> motions = new Dictionary<Target, Motion>();
        static readonly Dictionary<Target, Shape> shapes = new Dictionary<Target, Shape>();
        static readonly List<Target> gone = new List<Target>();
        static readonly List<(Target t, float distance)> picked = new List<(Target, float)>();
        static readonly Vector4[] sphereData = new Vector4[MaxSpheres], velocityData = new Vector4[MaxSpheres];
        static int sphereCount;
        static bool tracking, installed;
        static int sampledFrame = -1;
        static PhotoMotionPass instance;
        static Camera target;
        static float exposure;
        static Vector3 cameraVelocity;

        // Play mode runs without a domain reload: a photo mode open when it ended would keep blurring the next camera.
        [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.SubsystemRegistration)]
        static void ResetStatics() { target = null; exposure = 0f; motions.Clear(); shapes.Clear(); sampledFrame = -1; }

        Material material;

        /// <summary>Starts keeping the ships' velocities (once; the photo tools ask at their creation, before any freeze).</summary>
        public static void Track()
        {
            if (tracking) return;
            tracking = true;
            RenderPipelineManager.beginContextRendering += (_, _) => Sample();
        }

        /// <summary>A target's velocity (m/s) from the last frames the game time ran; zero for one never seen moving.</summary>
        public static Vector3 VelocityOf(Target t) => t != null && motions.TryGetValue(t, out var m) ? m.velocity : Vector3.zero;

        /// <summary>The player's ship's velocity (the target marked isPlayer).</summary>
        public static Vector3 PlayerVelocity()
        {
            foreach (var t in Target.All) if (t != null && t.isPlayer) return VelocityOf(t);
            return Vector3.zero;
        }

        /// <summary>Blurs what 'cam' sees over an exposure of 'seconds', the camera moving at 'camVelocity' m/s.</summary>
        public static void Set(Camera cam, float seconds, Vector3 camVelocity)
        {
            Install();
            target = cam;
            exposure = Mathf.Max(0f, seconds);
            cameraVelocity = camVelocity;
        }

        public static void Clear() { target = null; exposure = 0f; shapes.Clear(); }

        static void Sample()
        {
            if (Time.frameCount == sampledFrame) return;
            sampledFrame = Time.frameCount;
            float dt = Time.deltaTime;
            if (dt <= 0f) return;   // frozen: keep the velocities from before
            foreach (var t in Target.All)
            {
                if (t == null || !(t.isShip || t.isPlayer)) continue;
                var p = t.transform.position;
                if (!motions.TryGetValue(t, out var m)) { motions[t] = new Motion { position = p, frame = sampledFrame }; continue; }
                var v = (p - m.position) / dt;
                // Smoothed over a few frames (a frame's step jitters with the frame time); a jump keeps the old one.
                if (v.sqrMagnitude < TeleportSpeed * TeleportSpeed) m.velocity = Vector3.Lerp(m.velocity, v, 0.5f);
                m.position = p;
                m.frame = sampledFrame;
            }
            gone.Clear();
            foreach (var kv in motions) if (kv.Key == null || kv.Value.frame != sampledFrame) gone.Add(kv.Key);
            foreach (var t in gone) motions.Remove(t);
        }

        static void Install()
        {
            if (installed) return;
            installed = true;
            RenderPipelineManager.beginCameraRendering += OnBeginCamera;
        }

        static void OnBeginCamera(ScriptableRenderContext context, Camera cam)
        {
            if (exposure <= 0f || target == null || cam != target) return;
            var data = cam.GetUniversalAdditionalCameraData();
            if (data == null || data.scriptableRenderer == null) return;
            instance ??= new PhotoMotionPass();
            if (instance.material == null)
            {
                var shader = Resources.Load<Shader>("GoF2PostFx/PhotoMotion");
                if (shader == null || !shader.isSupported) return;
                instance.material = CoreUtils.CreateEngineMaterial(shader);
            }
            if (!Gather(cam)) return;   // nothing moves relative to the camera
            data.scriptableRenderer.EnqueuePass(instance);
        }

        /// <summary>The moving ships nearest the camera as spheres; false when nothing would blur.</summary>
        static bool Gather(Camera cam)
        {
            var eye = cam.transform.position;
            picked.Clear();
            foreach (var kv in motions)
                if (kv.Key != null && kv.Key.isActiveAndEnabled && kv.Value.velocity.sqrMagnitude > 0.01f)
                    picked.Add((kv.Key, Vector3.Distance(eye, kv.Key.transform.position)));
            picked.Sort((a, b) => a.distance.CompareTo(b.distance));
            sphereCount = Mathf.Min(picked.Count, MaxSpheres);
            for (int i = 0; i < sphereCount; i++)
            {
                var t = picked[i].t;
                var s = ShapeOf(t);
                var c = t.transform.TransformPoint(s.localCentre);
                sphereData[i] = new Vector4(c.x, c.y, c.z, s.radius);
                velocityData[i] = VelocityOf(t);
            }
            return sphereCount > 0 || cameraVelocity.sqrMagnitude > 0.01f;
        }

        /// <summary>A target's size from its renderers (once per photo session: the bounds are dear to gather every frame).</summary>
        static Shape ShapeOf(Target t)
        {
            if (shapes.TryGetValue(t, out var s)) return s;
            var rs = t.GetComponentsInChildren<Renderer>();
            Bounds b = new Bounds(t.transform.position, Vector3.one * (t.radius * 2f));
            bool any = false;
            foreach (var r in rs)
            {
                if (r is ParticleSystemRenderer || r is TrailRenderer || r is LineRenderer) continue;   // exhaust and trails reach far behind
                if (!any) { b = r.bounds; any = true; } else b.Encapsulate(r.bounds);
            }
            s = new Shape { localCentre = t.transform.InverseTransformPoint(b.center), radius = Mathf.Max(1f, b.extents.magnitude * 1.05f) };
            shapes[t] = s;
            return s;
        }

        PhotoMotionPass()
        {
            renderPassEvent = RenderPassEvent.BeforeRenderingPostProcessing - 1;   // ahead of PhotoBlurPass
            requiresIntermediateTexture = true;
            ConfigureInput(ScriptableRenderPassInput.Depth);
        }

        class PassData
        {
            public TextureHandle source, velocity, neighbour;
            public Material material;
            public int pass;
        }

        public override void RecordRenderGraph(RenderGraph renderGraph, ContextContainer frameData)
        {
            var resources = frameData.Get<UniversalResourceData>();
            if (resources.isActiveTargetBackBuffer || material == null) return;
            var source = resources.activeColorTexture;
            var depth = resources.cameraDepthTexture;
            if (!source.IsValid() || !depth.IsValid()) return;

            var sceneDesc = renderGraph.GetTextureDesc(source);
            int w = sceneDesc.width, h = sceneDesc.height;
            // The longest half-motion is a tile: a 24th of the picture's height at a 24th of a second or less (the streaks
            // up to a 12th of it), growing with a long exposure to a 5th at 2 s (streaks up to 40 % of the height).
            float longness = Mathf.Clamp01(Mathf.Log(Mathf.Max(1f, exposure * 24f)) / Mathf.Log(48f));
            int tile = Mathf.Max(8, Mathf.RoundToInt(h / Mathf.Lerp(24f, 5f, longness)));
            int tw = (w + tile - 1) / tile, th = (h + tile - 1) / tile;
            material.SetVectorArray(Spheres, sphereData);
            material.SetVectorArray(SphereVel, velocityData);
            material.SetInt(SphereCount, sphereCount);
            material.SetVector(CamVel, cameraVelocity);
            material.SetFloat(ExposureId, exposure);
            material.SetVector(Size, new Vector4(w, h, tw, th));
            material.SetFloat(Tile, tile);
            material.SetFloat(TileStride, Mathf.Max(1, Mathf.CeilToInt(tile / 24f)));

            var full = new TextureDesc(w, h)
            {
                colorFormat = GraphicsFormat.R16G16B16A16_SFloat,   // half-motion in pixels, eye depth
                filterMode = FilterMode.Point,
                wrapMode = TextureWrapMode.Clamp,
                name = "PhotoMotion Velocity",
            };
            var velocity = renderGraph.CreateTexture(full);
            var small = new TextureDesc(tw, th)
            {
                colorFormat = GraphicsFormat.R16G16_SFloat,
                filterMode = FilterMode.Point,
                wrapMode = TextureWrapMode.Clamp,
                name = "PhotoMotion TileMax",
            };
            var tileMax = renderGraph.CreateTexture(small);
            small.name = "PhotoMotion Neighbour";
            var neighbour = renderGraph.CreateTexture(small);

            Raster(renderGraph, "PhotoMotion Velocity", depth, default, default, velocity, PassVelocity);   // the depth as _DepthTex
            Raster(renderGraph, "PhotoMotion TileMax", velocity, default, default, tileMax, PassTileMax);
            Raster(renderGraph, "PhotoMotion Neighbour", tileMax, default, default, neighbour, PassNeighbour);

            // The scene is read and written by the gather: copy it first.
            sceneDesc.name = "PhotoMotion Scene";
            sceneDesc.clearBuffer = false;
            sceneDesc.msaaSamples = MSAASamples.None;
            sceneDesc.depthBufferBits = DepthBits.None;
            var scene = renderGraph.CreateTexture(sceneDesc);
            renderGraph.AddBlitPass(source, scene, Vector2.one, Vector2.zero, passName: "PhotoMotion Copy");
            Raster(renderGraph, "PhotoMotion Gather", scene, velocity, neighbour, source, PassGather);
        }

        void Raster(RenderGraph renderGraph, string name, TextureHandle from, TextureHandle velocity, TextureHandle neighbour,
                    TextureHandle to, int pass)
        {
            using var builder = renderGraph.AddRasterRenderPass<PassData>(name, out var data);
            data.source = from;
            data.velocity = velocity;
            data.neighbour = neighbour;
            data.material = material;
            data.pass = pass;
            builder.UseTexture(from);
            if (velocity.IsValid()) builder.UseTexture(velocity);
            if (neighbour.IsValid()) builder.UseTexture(neighbour);
            builder.SetRenderAttachment(to, 0);
            builder.SetRenderFunc((PassData d, RasterGraphContext ctx) =>
            {
                if (d.pass == PassVelocity) d.material.SetTexture(DepthTex, d.source);
                if (d.velocity.IsValid()) d.material.SetTexture(VelocityTex, d.velocity);
                if (d.neighbour.IsValid()) d.material.SetTexture(NeighbourTex, d.neighbour);
                Blitter.BlitTexture(ctx.cmd, d.source, new Vector4(1f, 1f, 0f, 0f), d.material, d.pass);
            });
        }
    }
}
