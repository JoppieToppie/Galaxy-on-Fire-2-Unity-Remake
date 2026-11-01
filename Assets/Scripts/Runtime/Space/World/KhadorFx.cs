// KhadorFx.cs
// Remake: the Khador Slipstream's way into interstellar space, the Khador Drive's animation drawn anew around a real hole: the
// original's beats and sheet with more in them (Flight.KhadorTimeline has its times, GoF2/KhadorFx its look): a beam lances out to a
// focus ahead of the ship, a star ignites there, the vein web unfurls round it, and the ring tears open with sparks flying out and
// arcs thrown off it. Inside the tear is the warp the next level opens on (OpenRift, GoF2/KhadorRift): the ship flies through
// with the camera held behind it (SystemJump). No whiteout: when the hole fills the view (RiftFilled) the frame is already the
// next level's, and SystemJump swaps. Every other Khador jump keeps the original fx (SystemJump.SpawnKhadorFx). The focus'
// gathering during the charge is a small object of its own (Gathering).

using GoF2Remake.Data;
using GoF2Remake.Flight;
using UnityEngine;

namespace GoF2Remake.World
{
    public class KhadorFx : MonoBehaviour
    {
        const float M = 0.05f;
        /// <summary>The prefab's scale in SystemJump.SpawnKhadorFx: the original's sizes (game units) are at it.</summary>
        const float BeamUnits = 44000f, BeamHalfWidthUnits = 340f, StarUnits = 1500f, FlashUnits = 12000f;
        /// <summary>How far the sparks fly (game units): parts 22-34 reach 4000-8500 at the prefab's scale of 2.</summary>
        const float SparkMinUnits = 8000f, SparkMaxUnits = 16000f, SparkUnits = 130f;
        const int SparkCount = 40;
        const int DiscSegments = 128;
        /// <summary>The ring's quad against its radius, room for the arcs; and how far in the tear's edge may stray (KhadorRim).</summary>
        const float RingSpan = 3.4f, RimMost = 1.3f, RimLeast = 0.78f;

        // The sheet's tiles (u, v, width, height; v up): the four-point star, the glow.
        static readonly Vector4 StarTile = new Vector4(0.285f, 0.285f, 0.18f, 0.18f), GlowTile = new Vector4(0.02f, 0.28f, 0.2f, 0.2f);

        static readonly int Level = Shader.PropertyToID("_Level"), Seconds = Shader.PropertyToID("_Seconds"), RadiusId = Shader.PropertyToID("_Radius"),
                            Width = Shader.PropertyToID("_Width"), Grow = Shader.PropertyToID("_Grow"), Hole = Shader.PropertyToID("_Hole"),
                            Twist = Shader.PropertyToID("_Twist"), Thick = Shader.PropertyToID("_Thick"), NearFade = Shader.PropertyToID("_NearFade"),
                            Size = Shader.PropertyToID("_Size"), Tint = Shader.PropertyToID("_Tint"), Tile = Shader.PropertyToID("_Tile"),
                            Stretch = Shader.PropertyToID("_Stretch"), Arcs = Shader.PropertyToID("_Arcs"), Fire = Shader.PropertyToID("_Fire"), ZTest = Shader.PropertyToID("_ZTest"), Sheet = Shader.PropertyToID("_Sheet"),
                            RiftRadius = Shader.PropertyToID("_RiftRadius"), RiftDepth = Shader.PropertyToID("_RiftDepth"),
                            RiftSeconds = Shader.PropertyToID("_RiftSeconds"), RiftTurn = Shader.PropertyToID("_RiftTurn"), RiftAxis = Shader.PropertyToID("_RiftAxis");

        readonly System.Collections.Generic.List<Object> owned = new System.Collections.Generic.List<Object>();
        Material beam, web, ring, star, glow, sparks, flash, streak, riftLook;
        Transform webPlane, ringPlane, riftPlane, flashPoint;
        float ms, rate = 1f;

        // ---- the hole (into interstellar space) ------------------------------------------------------------------
        bool rift;
        Transform ship;
        Quaternion nextHeading;
        float crossMs, shipHalfSpan, riftUnits;

        /// <summary>This fx is a hole into the space between the systems. 'nextHeading': the ship's rotation in the level to come;
        /// 'crossMs': when the ship reaches it, in the scene's time; the animation runs at RiftRate of that.</summary>
        public void OpenRift(Transform flyingShip, Quaternion nextHeading, float crossMs, float halfSpanUnits)
        {
            var look = RenderSettings.skybox;
            var shader = Resources.Load<Shader>("GoF2Sky/KhadorRift");
            if (look == null || shader == null) return;
            ship = flyingShip;
            this.nextHeading = nextHeading;
            this.crossMs = crossMs * KhadorTimeline.RiftRate;
            rate = KhadorTimeline.RiftRate;
            shipHalfSpan = halfSpanUnits;
            riftLook = Own(new Material(shader));
            riftLook.CopyMatchingPropertiesFromMaterial(look);
            riftLook.SetMatrix("_SkyRotation", look.GetMatrix("_SkyRotation"));
            foreach (var k in look.enabledKeywords) riftLook.EnableKeyword(k.name);
            riftPlane = Disc("Rift", riftLook, DiscMesh(DiscSegments));
            riftPlane.GetComponent<MeshRenderer>().enabled = false;
            rift = true;
        }

        /// <summary>The hole has filled what 'cam' sees: the next level's first frame is on screen.</summary>
        public bool RiftFilled(Camera cam)
        {
            if (!rift || cam == null || riftUnits <= 0f) return false;
            float distance = Vector3.Dot(transform.position - cam.transform.position, cam.transform.forward);
            return KhadorTimeline.Fills(riftUnits * RimLeast * M * transform.lossyScale.x, distance, Mathf.Tan(cam.fieldOfView * 0.5f * Mathf.Deg2Rad), cam.aspect);
        }

        public bool HasRift => rift;

        // ---- building ------------------------------------------------------------------------------------------------

        /// <summary>The animation at 'position', looking along 'rotation', to be opened by OpenRift; null without its shader.</summary>
        public static KhadorFx Create(Vector3 position, Quaternion rotation, Texture sheet)
        {
            var shader = Resources.Load<Shader>("GoF2Sky/KhadorFx");
            if (shader == null || sheet == null) return null;
            var fx = new GameObject("Khador fx").AddComponent<KhadorFx>();
            fx.transform.SetPositionAndRotation(position, rotation);
            fx.Build(shader, sheet);
            return fx;
        }

        static float Span => M;   // a part's size in metres at scale 1: every size below is game units x this

        void Build(Shader shader, Texture sheet)
        {
            beam = Look(shader, sheet, "_BEAM");
            web = Look(shader, sheet, "_WEB");
            ring = Look(shader, sheet, "_RING");
            star = Look(shader, sheet, "_SPRITE");
            glow = Look(shader, sheet, "_SPRITE");
            flash = Look(shader, sheet, "_SPRITE");
            streak = Look(shader, sheet, "_SPRITE");
            sparks = Look(shader, sheet, "_SPARKS");
            star.SetVector(Tile, StarTile);
            glow.SetVector(Tile, GlowTile);
            flash.SetVector(Tile, GlowTile);
            streak.SetVector(Tile, GlowTile);
            streak.SetFloat(Stretch, 7f);
            flash.SetFloat(Size, FlashUnits * Span);
            streak.SetFloat(Size, FlashUnits * 0.2f * Span);
            flash.renderQueue = streak.renderQueue = 3100;
            flash.SetFloat(ZTest, (float)UnityEngine.Rendering.CompareFunction.Always);
            streak.SetFloat(ZTest, (float)UnityEngine.Rendering.CompareFunction.Always);

            Add("Beam", BeamMesh(), beam);
            webPlane = Disc("Web", web, DiscMesh(DiscSegments));
            ringPlane = Disc("Ring", ring, DiscMesh(4));
            Add("Star", SpriteMesh(), star);
            Add("Glow", SpriteMesh(), glow);
            Add("Sparks", SparkMesh(), sparks);
            flashPoint = Add("Flash", SpriteMesh(), flash).transform;
            Add("Streak", SpriteMesh(), streak);
        }

        Material Look(Shader shader, Texture sheet, string keyword)
        {
            var m = Own(new Material(shader));
            m.EnableKeyword(keyword);
            m.SetTexture(Sheet, sheet);
            return m;
        }

        T Own<T>(T thing) where T : Object { owned.Add(thing); return thing; }

        MeshRenderer Add(string name, Mesh mesh, Material look)
        {
            var go = new GameObject(name);
            go.transform.SetParent(transform, false);
            go.AddComponent<MeshFilter>().sharedMesh = mesh;
            var r = go.AddComponent<MeshRenderer>();
            r.sharedMaterial = look;
            r.shadowCastingMode = UnityEngine.Rendering.ShadowCastingMode.Off;
            r.receiveShadows = false;
            return r;
        }

        Transform Disc(string name, Material look, Mesh mesh) => Add(name, mesh, look).transform;

        /// <summary>Along local -z from the focus (uv.x 0) to the tail (1); uv.y -1..1 across. Its tail reaches past any camera.</summary>
        Mesh BeamMesh()
        {
            const int rows = 48;
            var vertices = new Vector3[(rows + 1) * 2];
            var uv = new Vector2[vertices.Length];
            var triangles = new int[rows * 6];
            for (int i = 0; i <= rows; i++)
            {
                float s = i / (float)rows;
                s *= s;   // the rows crowd at the focus, where the eye is
                vertices[i * 2] = vertices[i * 2 + 1] = new Vector3(0f, 0f, -s * BeamUnits * Span);
                uv[i * 2] = new Vector2(s, -1f);
                uv[i * 2 + 1] = new Vector2(s, 1f);
                if (i == rows) break;
                int t = i * 6, v = i * 2;
                triangles[t] = v; triangles[t + 1] = v + 2; triangles[t + 2] = v + 1;
                triangles[t + 3] = v + 1; triangles[t + 4] = v + 2; triangles[t + 5] = v + 3;
            }
            var m = Own(new Mesh { name = "Khador beam", vertices = vertices, uv = uv, triangles = triangles });
            m.bounds = new Bounds(new Vector3(0f, 0f, -BeamUnits * Span / 2f), new Vector3(BeamHalfWidthUnits, BeamHalfWidthUnits, BeamUnits) * Span * 4f);
            return m;
        }

        /// <summary>The unit disc in the xy plane; uv its own -1..1.</summary>
        Mesh DiscMesh(int segments)
        {
            var vertices = new Vector3[segments + 1];
            var uv = new Vector2[segments + 1];
            var triangles = new int[segments * 3];
            for (int i = 0; i < segments; i++)
            {
                float a = i * 2f * Mathf.PI / segments + Mathf.PI / segments;
                var p = new Vector2(Mathf.Cos(a), Mathf.Sin(a)) * (1f / Mathf.Cos(Mathf.PI / segments));
                vertices[i + 1] = p;
                uv[i + 1] = p;
                triangles[i * 3] = 0;
                triangles[i * 3 + 1] = i + 1;
                triangles[i * 3 + 2] = (i + 1) % segments + 1;
            }
            return Own(new Mesh { name = "Khador disc", vertices = vertices, uv = uv, triangles = triangles });
        }

        Mesh SpriteMesh()
        {
            var m = Own(new Mesh { name = "Khador sprite" });
            m.vertices = new[] { new Vector3(-1f, -1f), new Vector3(1f, -1f), new Vector3(-1f, 1f), new Vector3(1f, 1f) };
            m.uv = new[] { new Vector2(0f, 0f), new Vector2(1f, 0f), new Vector2(0f, 1f), new Vector2(1f, 1f) };
            m.triangles = new[] { 0, 2, 1, 1, 2, 3 };
            m.bounds = new Bounds(Vector3.zero, Vector3.one * FlashUnits * Span * 4f);
            return m;
        }

        /// <summary>A quad per spark, all flown in the vertex shader: out of the focus in the plane facing the ship, a little
        /// back along the beam. The same sparks every time: the original's are a fixed burst.</summary>
        Mesh SparkMesh()
        {
            var seeded = new JavaRandom(1500);
            float Next() => seeded.NextInt(100000) / 100000f;
            var vertices = new Vector3[SparkCount * 4];
            var uv = new Vector2[vertices.Length];
            var way = new System.Collections.Generic.List<Vector3>(vertices.Length);
            var timing = new System.Collections.Generic.List<Vector4>(vertices.Length);
            var triangles = new int[SparkCount * 6];
            for (int i = 0; i < SparkCount; i++)
            {
                float angle = Next() * 2f * Mathf.PI, far = Mathf.Lerp(SparkMinUnits, SparkMaxUnits, Next());
                var dir = new Vector3(Mathf.Cos(angle) * far, Mathf.Sin(angle) * far, -far * 0.35f * Next()) * Span;
                float start = Mathf.Lerp(KhadorTimeline.SparksStartMs, KhadorTimeline.SparksEndMs - 500f, Mathf.Pow(Next(), 0.7f)) / 1000f;
                float life = Mathf.Lerp(0.45f, 0.8f, Next());
                float size = Mathf.Lerp(0.5f, 1.3f, Next()) * SparkUnits * Span;
                float seed = Next();
                for (int k = 0; k < 4; k++)
                {
                    var corner = new Vector2(k % 2 == 0 ? -1f : 1f, k < 2 ? -1f : 1f);
                    vertices[i * 4 + k] = corner;
                    uv[i * 4 + k] = corner;
                    way.Add(dir);
                    timing.Add(new Vector4(start, life, size, seed));
                }
                int t = i * 6, v = i * 4;
                triangles[t] = v; triangles[t + 1] = v + 2; triangles[t + 2] = v + 1;
                triangles[t + 3] = v + 1; triangles[t + 4] = v + 2; triangles[t + 5] = v + 3;
            }
            var m = Own(new Mesh { name = "Khador sparks", vertices = vertices, uv = uv, triangles = triangles });
            m.SetUVs(1, way);
            m.SetUVs(2, timing);
            m.bounds = new Bounds(Vector3.zero, Vector3.one * SparkMaxUnits * Span * 2.5f);
            return m;
        }

        // ---- playing -----------------------------------------------------------------------------------------------------

        void Update()
        {
            if (!rift) return;
            ms += Time.deltaTime * 1000f * rate;
            Tick(ms);
            if (ms >= KhadorTimeline.LengthMs) Destroy(gameObject);
        }

        void LateUpdate() => UpdateRift();

        void Tick(float at)
        {
            float seconds = at / 1000f, scale = transform.lossyScale.x;
            foreach (var m in new[] { beam, web, ring, sparks }) m.SetFloat(Seconds, seconds);

            float beamLevel = KhadorTimeline.Beam(at);
            beamLevel *= Mathf.Clamp01((crossMs + 60f - at) / 160f);   // spent as the ship goes through: all its light with it
            beam.SetFloat(Level, beamLevel);
            beam.SetFloat(Thick, BeamHalfWidthUnits * Span * scale * KhadorTimeline.BeamWidth(at));
            beam.SetFloat(NearFade, 35f * scale);

            // Into the hole the web draws back as the ship is drawn in, so the warp starting on the sky shows (KhadorTimeline.RiftWarp).
            float webBack = KhadorTimeline.RiftWarp(at / rate, crossMs / rate);
            web.SetFloat(Level, KhadorTimeline.Web(at) * (1f - 0.85f * webBack));
            web.SetFloat(Grow, KhadorTimeline.WebGrown(at));
            web.SetFloat(Twist, KhadorTimeline.WebTwist(at));
            webPlane.localScale = Vector3.one * KhadorTimeline.WebUnitsAcross * Span;

            float ringUnits = KhadorTimeline.RiftUnits(at, crossMs, shipHalfSpan);
            ringPlane.localScale = Vector3.one * Mathf.Max(ringUnits, 1f) * RingSpan * Span;
            ring.SetFloat(RadiusId, 1f / RingSpan);
            ring.SetFloat(Width, Mathf.Clamp(0.06f * (1f - Mathf.InverseLerp(0f, 1f, ringUnits / 12000f) * 0.7f), 0.012f, 0.06f) * 1.5f / RingSpan);
            ring.SetFloat(Level, KhadorTimeline.Ring(at) * 1.15f);
            ring.SetFloat(Arcs, Mathf.Max(0.2f, webBack));
            ring.SetFloat(Fire, 1f - Mathf.InverseLerp(crossMs - 60f, crossMs + 40f, at));
            riftUnits = ringUnits;
            web.SetFloat(Hole, ringUnits / KhadorTimeline.WebUnitsAcross * 0.98f);

            float starLevel = KhadorTimeline.Star(at);
            starLevel *= Mathf.Clamp01((crossMs + 40f - at) / 260f);   // gone as the ship reaches it: it would fill the view
            star.SetFloat(Level, starLevel * 1.5f);
            star.SetFloat(Size, StarUnits * Span * scale * (0.8f + 0.25f * Mathf.Sin(at * 0.031f) * Mathf.Sin(at * 0.017f)));
            glow.SetFloat(Level, starLevel * 0.55f);
            glow.SetFloat(Size, StarUnits * 2.4f * Span * scale);
            sparks.SetFloat(Level, Mathf.Clamp01((crossMs + 80f - at) / 120f));

            float flashLevel = KhadorTimeline.Flash(at, crossMs - 30f) * Mathf.Clamp01((crossMs + 70f - at) / 90f);
            flash.SetFloat(Level, flashLevel * 0.9f);
            flash.SetFloat(Size, FlashSize(FlashUnits * Span * scale * (0.4f + 0.6f * flashLevel)));
            streak.SetFloat(Level, flashLevel * 1.5f);
            streak.SetFloat(Size, FlashSize(FlashUnits * Span * scale) * 0.2f);
        }

        /// <summary>The flash's half size: never over a third of the camera's distance. Sized by the world it filled the view as
        /// the camera reached it, a white screen by another name.</summary>
        float FlashSize(float size)
        {
            var cam = Camera.main;
            return cam != null ? Mathf.Min(size, Vector3.Distance(cam.transform.position, flashPoint.position) * 0.33f + 1f) : size;
        }

        void UpdateRift()
        {
            if (!rift) return;
            var cam = Camera.main;
            var mesh = riftPlane.GetComponent<MeshRenderer>();
            mesh.enabled = riftUnits > 0f;
            if (cam == null || ship == null || !mesh.enabled) return;
            float scale = transform.lossyScale.x;
            riftPlane.localScale = Vector3.one * riftUnits * RimMost * Span;
            Shader.SetGlobalFloat(RiftRadius, 1f / RimMost);
            Shader.SetGlobalFloat(RiftSeconds, ms / 1000f);
            // The orbit goes behind the plane past the ship's nose, or the hole's own, whichever is further.
            var toPlane = cam.transform.forward;
            float plane = Vector3.Dot(transform.position - cam.transform.position, toPlane);
            float past = Vector3.Dot(ship.position - cam.transform.position, toPlane) + (shipHalfSpan * 2f + 400f) * Span * scale;
            Shader.SetGlobalFloat(RiftDepth, Mathf.Max(plane, past, cam.nearClipPlane * 2f));
            var turn = nextHeading * Quaternion.Inverse(ship.rotation);
            Shader.SetGlobalMatrix(RiftTurn, Matrix4x4.Rotate(turn));
            Shader.SetGlobalVector(RiftAxis, nextHeading * Vector3.forward);
        }

        void OnDestroy()
        {
            foreach (var o in owned) if (o != null) Destroy(o);
        }

        // ---- the charge ----------------------------------------------------------------------------------------------------

        /// <summary>The focus gathering ahead of the ship over the end of the charge: the star the jump's beam will strike.</summary>
        public class Gathering : MonoBehaviour
        {
            Material star, glow;
            Mesh mesh;
            float rate;

            public static Gathering Create(Transform ship, Vector3 local, Texture sheet)
            {
                var shader = Resources.Load<Shader>("GoF2Sky/KhadorFx");
                if (shader == null || sheet == null) return null;
                var go = new GameObject("Khador focus");
                go.transform.SetParent(ship, false);
                go.transform.localPosition = local;
                var g = go.AddComponent<Gathering>();
                g.mesh = new Mesh { name = "Khador sprite" };
                g.mesh.vertices = new[] { new Vector3(-1f, -1f), new Vector3(1f, -1f), new Vector3(-1f, 1f), new Vector3(1f, 1f) };
                g.mesh.uv = new[] { new Vector2(0f, 0f), new Vector2(1f, 0f), new Vector2(0f, 1f), new Vector2(1f, 1f) };
                g.mesh.triangles = new[] { 0, 2, 1, 1, 2, 3 };
                g.mesh.bounds = new Bounds(Vector3.zero, Vector3.one * 400f);
                g.star = g.Sprite("Star", shader, sheet, StarTile);
                g.glow = g.Sprite("Glow", shader, sheet, GlowTile);
                return g;
            }

            Material Sprite(string name, Shader shader, Texture sheet, Vector4 tile)
            {
                var m = new Material(shader);
                m.EnableKeyword("_SPRITE");
                m.SetTexture(Sheet, sheet);
                m.SetVector(Tile, tile);
                m.SetFloat(Level, 0f);
                var go = new GameObject(name);
                go.transform.SetParent(transform, false);
                go.AddComponent<MeshFilter>().sharedMesh = mesh;
                var r = go.AddComponent<MeshRenderer>();
                r.sharedMaterial = m;
                r.shadowCastingMode = UnityEngine.Rendering.ShadowCastingMode.Off;
                r.receiveShadows = false;
                return m;
            }

            public void SetCharge(float chargeRate)
            {
                rate = KhadorTimeline.ChargeFocus(chargeRate);
                float beat = 0.8f + 0.2f * Mathf.Sin(Time.time * 31f);
                star.SetFloat(Level, rate * 2.2f * beat);
                star.SetFloat(Size, Mathf.Lerp(0.15f, 0.8f, rate) * StarUnits * Span);
                glow.SetFloat(Level, rate * 1.1f);
                glow.SetFloat(Size, Mathf.Lerp(0.3f, 1f, rate) * StarUnits * 1.5f * Span);
            }

            void OnDestroy()
            {
                if (star != null) Destroy(star);
                if (glow != null) Destroy(glow);
                if (mesh != null) Destroy(mesh);
            }
        }
    }
}
