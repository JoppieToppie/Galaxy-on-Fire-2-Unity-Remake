// NebulaClouds.cs
// Remake (2026-11-14, the user: EVERSPACE 2's sky methods in the base game, without ES2's files): Options > Graphics "Nebula
// clouds" (Settings.NebulaClouds, off by default: the original has none). Clouds of gas round the orbit, in its own sky's
// colours, that the player flies through and past with real parallax, as EVERSPACE 2's local nebulae (BP_Skybox_Nebula_Local,
// read from its compiled script): of 101 random points in a sphere of the cloud's radius, those under HalfHeight x radius x
// r^2 off its plane (r random 0..1: a flattened disc, densest in its middle) each become a flat sprite at a random turn, 1-3 x
// its piece size, with its own random values (the mask's scale and offset) and one of the mask's three channels; drawn by
// GoF2/NebulaCloud (M_Nebula_Local_Parent's look). ES2's levels have 1-5 of them (most two), 20 km across with their middles a
// few km out, density 1, their own colours; here 0-3 by how much of the orbit's sky its nebula covers, sized to GoF2's orbits
// (the level's far plane is 15 km), their colours the main colours of the orbit's nebula sky (nebula_clouds.json), all seeded
// by the station, so an orbit's clouds are the same on every visit.
using System.Collections.Generic;
using GoF2Remake.Data;
using UnityEngine;

namespace GoF2Remake.World
{
    [Unity.Scripting.LifecycleManagement.NoAutoStaticsCleanup]
    public sealed class NebulaClouds : MonoBehaviour
    {
        public const int Candidates = 101;          // ES2: 0..100 (51 on consoles)
        public const float HalfHeight = 0.3f;       // ES2's default
        public const float MinRadius = 6000f, MaxRadius = 14000f;   // m
        /// <summary>GoF2/NebulaCloud's mean opacity over a piece (its radial falloff squared, ES2's curves on the mask): 2.4e-4 with the
        /// remake's masks, 3.7e-4 with ES2's (measured over 60 pieces each). ES2 brings its crushed opacity back to a glow with
        /// Brightness 10000 under its auto exposure; here each cloud's light is set so its mean over its disc is its target
        /// (the sky region it stands for, or its sky's brightness): ES2's exposure, done per cloud.</summary>
        public const float PieceMeanOpacity = 3e-4f;
        /// <summary>A piece's mean share of its area seen, at a random turn, after the edge-on fade (FresnelFadeMin 0.3):
        /// the integral of f x saturate((f - 0.3) / 0.7) over f in 0..1.</summary>
        public const float SeenShare = 0.268f;
        public const float PieceShare = 0.25f;     // a piece's half-size per (1-3 x) the radius: ES2's 5 km planes at 1-3 x radius / 10 km

        [System.Serializable] public sealed class Rgbw { public float r, g, b, weight; }
        [System.Serializable] public sealed class Sky { public float coverage, brightness; public Rgbw[] colors; }
        [System.Serializable] sealed class Table { public Sky[] nebulae; }

        static Table table;
        [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.SubsystemRegistration)] static void ResetStatics() => table = null;

        /// <summary>The cloud look of GoF2's nebula sky 'index' (0-18); null without one.</summary>
        public static Sky Of(int index)
        {
            if (table == null)
            {
                var asset = Resources.Load<TextAsset>("GoF2SkyLayers/nebula_clouds");
                table = asset != null ? JsonUtility.FromJson<Table>(asset.text) : new Table { nebulae = new Sky[0] };
            }
            return index >= 0 && index < table.nebulae.Length ? table.nebulae[index] : null;
        }

        /// <summary>How many clouds an orbit under a sky with this coverage has: none under the barest skies, three in the
        /// ones that fill the sky.</summary>
        public static int CountFor(float coverage) => coverage >= 0.5f ? 3 : coverage >= 0.06f ? 2 : coverage >= 0.025f ? 1 : 0;

        public struct Piece { public Vector3 position; public Quaternion turn; public float size; public Vector4 random; public int channel; }

        /// <summary>One cloud's pieces, ES2's placement (its radius, in its own frame: the disc's plane is its xy).</summary>
        public static List<Piece> Pieces(System.Random rng, float radius)
        {
            var list = new List<Piece>();
            for (int i = 0; i < Candidates; i++)
            {
                Vector3 p;
                do p = new Vector3(Next(rng) * 2f - 1f, Next(rng) * 2f - 1f, Next(rng) * 2f - 1f);
                while (p.sqrMagnitude > 1f);
                p *= radius;
                float r = Next(rng);
                if (Mathf.Abs(p.z) >= HalfHeight * radius * r * r) continue;
                var turn = Quaternion.Euler(Next(rng) * 360f, Next(rng) * 360f, Next(rng) * 360f);
                float size = Mathf.Lerp(1f, 3f, Next(rng)) * radius * PieceShare;
                var random = new Vector4(Next(rng), Next(rng), Next(rng), Next(rng));
                list.Add(new Piece { position = p, turn = turn, size = size, random = random, channel = rng.Next(3) });
            }
            return list;
        }

        static float Next(System.Random rng) => (float)rng.NextDouble();

        /// <summary>Whether an orbit has near clouds: the option, or the sky styles that are EVERSPACE 2's (ES2 style and
        /// Combined: its locations have their local nebulae round them).</summary>
        public static bool NearClouds => Settings.NebulaClouds || Settings.SkyStyle == Settings.SkyEs2 || Settings.SkyStyle == Settings.SkyCombined;

        /// <summary>Whether its sky's bright regions are distant clouds: ES2 style (with its layers drawn, Live mod skies), as
        /// ES2 draws a sector's nebulae from a location (BP_EnvironmentGenerator.ProcessSpaceObjects: each a local nebula at its
        /// direction and scaled distance; the sky sphere has only the stars and a faint sky-wide nebula).</summary>
        public static bool RegionClouds => Settings.SkyStyle == Settings.SkyEs2 && Settings.LiveSkies;

        /// <summary>Distant clouds' distance (m): far past the far plane (15 km), so they barely move as the ship does; drawn at
        /// the far plane in their own direction, so their size on the sky is kept.</summary>
        public const float RegionDistance = 45000f;

        /// <summary>The orbit's clouds (null: none: off, the space between the systems, or a sky too bare for any).</summary>
        public static NebulaClouds Spawn(OrbitLayout layout, Transform parent = null)
        {
            if (layout == null || layout.interstellar) return null;
            var sky = Of(layout.systemTexture);
            if (sky == null || sky.colors == null || sky.colors.Length == 0) return null;
            int near = NearClouds ? CountFor(sky.coverage) : 0;
            var regions = RegionClouds ? GoF2SkyLayers.Of(layout.systemTexture) : null;
            if (near == 0 && (regions?.regions == null || regions.regions.Length == 0)) return null;
            var shader = Resources.Load<Shader>("GoF2SkyLayers/NebulaCloud");
            var masks = Modding.ModTextures.SkyLayer("cloud_masks") ?? Resources.Load<Texture2D>("GoF2SkyLayers/cloud_masks");   // (a mod's textures/cloud_masks: the ES2 mod's, ES2's own)
            if (shader == null || masks == null) return null;

            var go = new GameObject("NebulaClouds");
            go.transform.SetParent(parent, false);
            var clouds = go.AddComponent<NebulaClouds>();
            var rng = new System.Random(layout.stationIndex * 7919 + layout.systemTexture * 104729 + 17);
            var mesh = new Builder();
            (Vector3 centre, float radius, Color color)? fogCloud = null;
            for (int c = 0; c < near; c++)
            {
                float radius = Mathf.Lerp(MinRadius, MaxRadius, Next(rng));
                // its middle a few km out (ES2's median 3.6 km for 20 km clouds), its plane at a random turn
                var dir = new Vector3(Next(rng) * 2f - 1f, (Next(rng) * 2f - 1f) * 0.5f, Next(rng) * 2f - 1f).normalized;
                var centre = dir * radius * Mathf.Lerp(0.15f, 0.6f, Next(rng));
                var plane = Quaternion.Euler(Mathf.Lerp(-30f, 30f, Next(rng)), Next(rng) * 360f, Mathf.Lerp(-30f, 30f, Next(rng)));
                // its colour one of the sky's regions' two main hues (the sky's palette is ruled by its bright cores: 007's teal
                // and brown came out a beige yellow), its thin edges the sky's main colour (ES2's system colour: the material's)
                var tint = Pick(sky.colors, Next(rng));
                var hue = Vivid(new Color(tint.r, tint.g, tint.b), Saturation);
                var regionLook = GoF2SkyLayers.Of(layout.systemTexture)?.regions;
                if (regionLook != null && regionLook.Length > 0)
                {
                    var r = regionLook[rng.Next(regionLook.Length)];
                    hue = Vivid(Rgb(Next(rng) < 0.6f ? r.colorA ?? r.color : r.colorB ?? r.color), Saturation);
                }
                float target = Mathf.Lerp(0.6f, 1.2f, Next(rng)) * Mathf.Clamp(sky.brightness, 0.05f, 0.6f) * 0.5f;
                mesh.Cloud(rng, centre, plane, radius, hue, null, target);
                if (c == 0) fogCloud = (centre, radius, hue * Mathf.Clamp(sky.brightness, 0.05f, 0.6f));
            }
            if (regions?.regions != null)
            {
                // the sky's bright regions as distant local nebulae: in their direction (the sky's turn: the baked cube's frame
                // to the world), as wide on the sky as the region, its plane facing the orbit, its own colour
                var turn = OrbitBuilder.SkyRotation(layout);
                foreach (var r in regions.regions)
                {
                    if (r.dir == null || r.dir.Length < 3) continue;
                    var dir = (turn * new Vector3(r.dir[0], r.dir[1], r.dir[2])).normalized;
                    float radius = RegionDistance * Mathf.Tan(Mathf.Clamp(r.radius, 4f, 40f) * Mathf.Deg2Rad);
                    // its two main hues (the heavier its own, the other its thin edges': an average of a brown and teal region was a
                    // beige that saturated to yellow); its light the region's own (the painted sky's brightness there)
                    var a = Rgb(r.colorA ?? r.color); var b = Rgb(r.colorB ?? r.colorA ?? r.color);
                    mesh.Cloud(rng, dir * RegionDistance, Quaternion.LookRotation(dir), radius, Vivid(a, Saturation), Vivid(b, Saturation), Mathf.Clamp(r.brightness, 0.02f, 1f));
                }
            }
            var built = mesh.Mesh();
            var look = new Material(shader) { name = "NebulaClouds" };
            look.SetTexture("_Masks", masks);
            var sun = OrbitLayout.DirToUnity(layout.lightDirection).normalized;
            look.SetVector("_SunDir", new Vector4(sun.x, sun.y, sun.z, 0f));
            look.SetColor("_SunColor", layout.sunColor.maxColorComponent > 0f ? layout.sunColor : Color.white);
            // the "system colour" ES2's local nebulae turn to at their thin edges: the sky's main colour
            var main = GoF2SkyLayers.Of(layout.systemTexture)?.main;
            look.SetColor("_SysColor", main != null && main.Length >= 3 ? Vivid(new Color(main[0], main[1], main[2]), Saturation) : Color.gray);
            // ES2's fog inside a nebula (BP_EnvironmentGenerator: a volumetric fog field where the location lies within a
            // nebula's radius, its density (1 - d / r)^0.5 x 0.05): here the scene's fog, where the system has none of its own
            if (fogCloud is { } f && !layout.fog)
            {
                float inside = Mathf.Sqrt(1f - Mathf.Clamp01(f.centre.magnitude / f.radius));
                if (inside > 0.05f)
                {
                    GoF2Remake.World.SpaceHooks.SceneFog(true);
                    RenderSettings.fogMode = FogMode.Linear;
                    RenderSettings.fogStartDistance = 0f;
                    RenderSettings.fogEndDistance = FogEnd(f.radius, inside);
                    RenderSettings.fogColor = f.color;
                    clouds.fog = true;
                }
            }
            go.AddComponent<MeshFilter>().sharedMesh = built;
            var renderer = go.AddComponent<MeshRenderer>();
            renderer.sharedMaterial = look;
            renderer.shadowCastingMode = UnityEngine.Rendering.ShadowCastingMode.Off;
            renderer.receiveShadows = false;
            clouds.mesh = built; clouds.look = look;
            return clouds;
        }

        /// <summary>The clouds' sprites as one mesh.</summary>
        sealed class Builder
        {
            readonly List<Vector3> vertices = new(), normals = new();
            readonly List<Vector2> uvs = new(), sizes = new();
            readonly List<Vector4> randoms = new(), edges = new();
            readonly List<Color> colors = new();
            readonly List<int> tris = new();

            /// <summary>One cloud, its light set so its mean over its disc is 'target' (linear).</summary>
            /// <param name="edgeHue">its thin edges' colour; null: the material's system colour (the sky's main colour)</param>
            public void Cloud(System.Random rng, Vector3 centre, Quaternion plane, float radius, Color hue, Color? edgeHue, float target)
            {
                var pieces = Pieces(rng, radius);
                float seen = 0f;
                foreach (var piece in pieces) seen += 4f * piece.size * piece.size * SeenShare;
                float overlap = seen / (Mathf.PI * radius * radius);
                float light = target / Mathf.Max(PieceMeanOpacity * overlap, 1e-6f);
                var tint = hue * light;
                foreach (var piece in pieces)
                {
                    var at = centre + plane * piece.position;
                    var turn = plane * piece.turn;
                    var right = turn * Vector3.right * piece.size; var up = turn * Vector3.up * piece.size; var n = turn * Vector3.forward;
                    int b = vertices.Count;
                    vertices.Add(at - right - up); vertices.Add(at + right - up); vertices.Add(at + right + up); vertices.Add(at - right + up);
                    uvs.Add(new Vector2(0, 0)); uvs.Add(new Vector2(1, 0)); uvs.Add(new Vector2(1, 1)); uvs.Add(new Vector2(0, 1));
                    // dimmer toward the cloud's rim (from half its radius out), so a cloud fades out instead of ending at an edge
                    float taper = Mathf.SmoothStep(1f, 0f, Mathf.InverseLerp(0.5f, 1f, piece.position.magnitude / radius));
                    var col = new Color(tint.r * taper, tint.g * taper, tint.b * taper, piece.channel * 0.5f);
                    var edge = edgeHue is Color e ? new Vector4(e.r, e.g, e.b, 1f) : Vector4.zero;
                    for (int k = 0; k < 4; k++) { normals.Add(n); randoms.Add(piece.random); colors.Add(col); sizes.Add(new Vector2(piece.size, 0f)); edges.Add(edge); }
                    tris.Add(b); tris.Add(b + 2); tris.Add(b + 1); tris.Add(b); tris.Add(b + 3); tris.Add(b + 2);
                }
            }

            public Mesh Mesh()
            {
                var mesh = new Mesh { name = "NebulaClouds", indexFormat = UnityEngine.Rendering.IndexFormat.UInt32 };
                mesh.SetVertices(vertices); mesh.SetNormals(normals); mesh.SetUVs(0, uvs); mesh.SetUVs(1, randoms); mesh.SetUVs(2, sizes); mesh.SetUVs(3, edges); mesh.SetColors(colors);
                mesh.SetTriangles(tris, 0);
                mesh.bounds = new Bounds(Vector3.zero, Vector3.one * 1e6f);   // (always drawn: the far pieces are pulled inside the far plane)
                return mesh;
            }
        }

        /// <summary>A cloud's colour: one of its sky's main colours, the less common ones as likely as the first (a cloud in
        /// exactly the sky's own colour vanishes against it; ES2's local nebulae have their own).</summary>
        static Rgbw Pick(Rgbw[] colors, float u)
        {
            float total = 0f;
            foreach (var c in colors) total += Mathf.Sqrt(Mathf.Max(c.weight, 0f));
            u *= total;
            foreach (var c in colors) { float w = Mathf.Sqrt(Mathf.Max(c.weight, 0f)); if (u < w) return c; u -= w; }
            return colors[colors.Length - 1];
        }

        /// <summary>The fog inside a cloud: fully hazed at 3 x its radius at its middle, thinner toward its edge (ES2's
        /// (1 - d / r)^0.5), 6-40 km.</summary>
        public static float FogEnd(float radius, float inside) => Mathf.Clamp(radius * 3f / Mathf.Max(inside, 0.05f), 6000f, 40000f);

        /// <summary>How much the clouds' colours are saturated: GoF2's are pale, ES2's vivid; 1.6 drifted the hue of mixed regions.</summary>
        public const float Saturation = 1.3f;

        static Color Rgb(float[] a) => a != null && a.Length >= 3 ? new Color(a[0], a[1], a[2]) : Color.white;

        static Color Vivid(Color c, float amount)
        {
            float l = 0.2126f * c.r + 0.7152f * c.g + 0.0722f * c.b;
            return new Color(Mathf.Max(0f, l + (c.r - l) * amount), Mathf.Max(0f, l + (c.g - l) * amount), Mathf.Max(0f, l + (c.b - l) * amount), 1f);
        }

        Mesh mesh;
        Material look;
        /// <summary>Whether it hazed the scene (ES2's fog inside a nebula); the next orbit's sky sets its own fog.</summary>
        public bool fog;

        void OnDestroy()
        {
            if (mesh != null) Destroy(mesh);
            if (look != null) Destroy(look);
        }
    }
}
