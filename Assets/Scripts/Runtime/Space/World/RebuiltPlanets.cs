// RebuiltPlanets.cs
// Remake (the user's request 2026-11-06, Options > Graphics "3D planets", Settings.RebuiltPlanets): the game's orbit planets
// rebuilt as lit spheres from their pngs (the planet_rebuild tools: each png's light taken out, its surface carried round the
// sphere, its light curve, rim, halo and soft edge measured), in place of the flat images wherever a planet shows: the orbit's
// backdrop (World.Backdrop, lit as its png is, so the orbit looks as it always did), the star map's system view (UI.StarMap,
// lit by the map's sun) and the space between the systems (World.InterstellarField, lit by their own star). planet_void
// keeps its png (its rebuild isn't done), and so does a planet a mod gives its own image.
// Each planet is Resources/GoF2Planets3D/<key>/ (key: the png's number, 000-026; 025 brings its moon 025m): planet.json and
// its maps, drawn by GoF2/RebuiltPlanet(Backdrop) on a quad facing the camera round the sphere (RebuiltPlanet.hlsl). A body's
// object: the sphere's centre, its radius the object's scale, its axes the png's (x right, y up, the png's viewer at -z).

using System.Collections.Generic;
using Newtonsoft.Json.Linq;
using UnityEngine;

namespace GoF2Remake.World
{
    [Unity.Scripting.LifecycleManagement.NoAutoStaticsCleanup]
    public static class RebuiltPlanets
    {
        public const string Folder = "GoF2Planets3D";

        /// <summary>The setting: the planets drawn as the rebuilt spheres.</summary>
        public static bool Enabled => Data.Settings.RebuiltPlanets;

        /// <summary>The rebuilt planet of a backdrop image (planet_007_big, v_planet_021_small, sn_planet_025_big), null for one
        /// with none (planet_void, a mod's "mod:" image, a sun).</summary>
        public static string KeyOfTexture(string texture)
        {
            if (string.IsNullOrEmpty(texture) || texture.StartsWith("mod:")) return null;
            string t;
            if (texture.StartsWith("sn_planet_")) t = texture.Substring(10);
            else if (texture.StartsWith("v_planet_")) t = texture.Substring(9);
            else if (texture.StartsWith("planet_")) t = texture.Substring(7);
            else return null;
            int us = t.IndexOf('_');
            if (us != 3 || !(t.EndsWith("_big") || t.EndsWith("_small"))) return null;
            string key = t.Substring(0, 3);
            return Has(key) ? key : null;
        }

        /// <summary>The rebuilt planet of a station's planet number (Station.textureIndex, the star map's galaxymap_planet_N), null
        /// for 23 (the Void) and any without one.</summary>
        public static string KeyOfIndex(int textureIndex)
        {
            if (textureIndex < 0 || textureIndex == 23) return null;
            string key = textureIndex.ToString("000");
            return Has(key) ? key : null;
        }

        public static bool Has(string key) => Get(key) != null;

        /// <summary>The planet draws its own ring (009, 016, 021).</summary>
        public static bool HasRing(string key) => Get(key)?.ring != null;

        /// <summary>A ringed planet stood upright in a model's frame: its ring's pole on the model's up (the star map's ringed models
        /// keep their ring flat in their orbit's plane); identity for any other.</summary>
        public static Quaternion Upright(string key)
        {
            var l = Get(key);
            if (l?.ring == null) return Quaternion.identity;
            var pole = new Vector3(l.ringPole.x, l.ringPole.y, -l.ringPole.z);   // (the png's frame -> the object's: its viewer at -z)
            return Quaternion.FromToRotation(pole.normalized, Vector3.up);
        }

        /// <summary>A model's planet hull's radius in 'root''s units (its mesh's half width; the map's planets are spheres round their origin).</summary>
        public static float HullRadius(MeshRenderer hull, Transform root)
        {
            var mesh = hull.GetComponent<MeshFilter>()?.sharedMesh;
            float r = mesh != null ? Mathf.Max(mesh.bounds.extents.x, Mathf.Max(mesh.bounds.extents.y, mesh.bounds.extents.z)) : 1600f;
            float k = Mathf.Abs(hull.transform.lossyScale.x) / Mathf.Max(1e-9f, Mathf.Abs(root.lossyScale.x));
            return r * k;
        }

        /// <summary>Where the disc lies in its png (its centre, in the image's width from the top left; its radius in widths).</summary>
        public static bool Disc(string key, out Vector2 centre, out float radius)
        {
            var l = Get(key);
            centre = l != null ? l.centre : new Vector2(0.5f, 0.5f);
            radius = l != null ? l.radius : 0.45f;
            return l != null;
        }

        /// <summary>The planet's material for 'key' (its own copy; null without the planet or the shader). 'backdrop': the orbit's
        /// backdrop pass (GoF2/RebuiltPlanetBackdrop), else the scene's (GoF2/RebuiltPlanet).</summary>
        public static Material MakeMaterial(string key, bool backdrop)
        {
            var l = Get(key);
            if (l == null) return null;
            var shader = backdrop ? (backdropShader ??= Resources.Load<Shader>($"{Folder}/RebuiltPlanetBackdrop")) : (sceneShader ??= Resources.Load<Shader>($"{Folder}/RebuiltPlanet"));
            if (shader == null) return null;
            var m = new Material(shader) { name = "Rebuilt planet " + key, renderQueue = backdrop ? 2901 : 3100 };   // (the scene's after the transparent glows: a sun's glow drawn over the ring washed it out; the body hides what lies behind it by its depth)
            m.SetTexture("_Albedo", l.albedo);
            m.SetTexture("_Face", l.face);
            m.SetTexture("_Corr", l.corr);
            if (l.emission != null) m.SetTexture("_Emission", l.emission);
            if (l.night != null) m.SetTexture("_Night", l.night);
            if (l.glow != null) m.SetTexture("_Glow", l.glow);
            m.SetTexture("_Curves", l.curves);
            m.SetTexture("_Halo", l.halo);
            m.SetTexture("_Cover", l.cover);
            if (l.ring != null) m.SetTexture("_Ring", l.ring);
            if (l.ringProfile != null) { m.SetTexture("_RingProfile", l.ringProfile); m.SetVector("_RingProfileR", l.ringProfileR); }
            m.SetVector("_LightDir", new Vector4(0f, 0f, -1f, 1f));   // (the png's own light, until told otherwise)
            m.SetVector("_FaceLight", l.faceLight);
            m.SetVector("_Curve", l.curve);
            m.SetVector("_Knobs", l.knobs);
            m.SetVector("_RimColor", l.rimColor);
            m.SetVector("_RimShape", l.rimShape);
            m.SetVector("_Grade", l.grade);
            m.SetVector("_GradeGain", l.gradeGain);
            m.SetVector("_HaloR", l.haloR);
            m.SetVector("_Layers", new Vector4(l.emission != null ? 1f : 0f, l.night != null ? 1f : 0f, l.glow != null ? 1f : 0f, 0f));
            m.SetVector("_RingPole", l.ring != null ? l.ringPole : Vector4.zero);
            m.SetVector("_RingR", l.ring != null ? l.ringR : new Vector4(0.4f, 1.12f, 1.16f, 1f));
            m.SetVector("_RingE1", l.ringE1); m.SetVector("_RingE2", l.ringE2);
            m.SetVector("_RingM0", l.ringM0); m.SetVector("_RingM1", l.ringM1); m.SetVector("_RingM2", l.ringM2);
            m.SetFloat("_FaceHalf", l.faceHalf);
            return m;
        }

        /// <summary>The planet as seen from its png's own side lit as its png is ('mirror': the png's mirror image, its light too).</summary>
        public static void UsePngLight(Material m) { if (m != null) m.SetVector("_LightDir", new Vector4(0f, 0f, -1f, 1f)); }
        /// <summary>Lit from the world direction 'towardLight'.</summary>
        public static void UseLight(Material m, Vector3 towardLight) { if (m != null) m.SetVector("_LightDir", new Vector4(towardLight.x, towardLight.y, towardLight.z, 0f)); }
        /// <summary>Lit from the star map's sun (GoF2/MapPlanet's global _StarMapSunPos).</summary>
        public static void UseStarMapSun(Material m) { if (m != null) m.SetVector("_LightDir", new Vector4(0f, 0f, 0f, 2f)); }

        /// <summary>A planet's companions in its png (025's moon): their key, their centre in the planet's object units (its frame,
        /// 'mirror' as the planet's) and their radius in the planet's.</summary>
        public static List<(string key, Vector3 at, float radius)> Companions(string key, bool mirror)
        {
            var list = new List<(string, Vector3, float)>();
            var l = Get(key);
            if (l == null) return list;
            foreach (var c in l.companions)
                if (Has(c.key)) list.Add((c.key, new Vector3(c.at.x * (mirror ? -1f : 1f), c.at.y, -c.at.z), c.radius));
            return list;
        }

        /// <summary>A planet's drawing: 'parent''s child at its origin (the sphere's centre; its radius the parent's units x 'radius'),
        /// its companions under it. Its materials go with it (RebuiltPlanetOwner). Null without the planet.</summary>
        public static GameObject Spawn(string key, Transform parent, float radius, bool backdrop, int layer, out List<Material> materials)
        {
            materials = new List<Material>();
            var main = MakeMaterial(key, backdrop);
            if (main == null) return null;
            var go = Part("Rebuilt planet " + key, parent, main, layer);
            go.transform.localScale = Vector3.one * radius;
            materials.Add(main);
            foreach (var (ck, at, cr) in Companions(key, false))
            {
                var cm = MakeMaterial(ck, backdrop);
                if (cm == null) continue;
                var c = Part("Rebuilt planet " + ck, go.transform, cm, layer);
                c.transform.localPosition = at;
                c.transform.localScale = Vector3.one * cr;
                materials.Add(cm);
            }
            go.AddComponent<RebuiltPlanetOwner>().materials = materials;
            return go;
        }

        static GameObject Part(string name, Transform parent, Material m, int layer)
        {
            var go = new GameObject(name) { layer = layer };
            go.transform.SetParent(parent, false);
            go.AddComponent<MeshFilter>().sharedMesh = Quad;
            var r = go.AddComponent<MeshRenderer>();
            r.sharedMaterial = m;
            r.shadowCastingMode = UnityEngine.Rendering.ShadowCastingMode.Off;
            r.receiveShadows = false;
            r.lightProbeUsage = UnityEngine.Rendering.LightProbeUsage.Off;
            r.reflectionProbeUsage = UnityEngine.Rendering.ReflectionProbeUsage.Off;
            return go;
        }

        static Mesh quad;
        /// <summary>The quad the shader turns toward the camera round the sphere (its corners only; never culled by its own size).</summary>
        public static Mesh Quad
        {
            get
            {
                if (quad != null) return quad;
                quad = new Mesh { name = "RebuiltPlanetQuad" };
                quad.vertices = new[] { new Vector3(-1f, -1f, 0f), new Vector3(1f, -1f, 0f), new Vector3(1f, 1f, 0f), new Vector3(-1f, 1f, 0f) };
                quad.triangles = new[] { 0, 2, 1, 0, 3, 2 };
                quad.bounds = new Bounds(Vector3.zero, Vector3.one * 1e4f);
                return quad;
            }
        }

        // ---- the planets' files

        sealed class Look
        {
            public Texture2D albedo, face, corr, emission, night, glow, ring, ringProfile, curves, halo, cover;
            public Vector4 ringProfileR;
            public Vector2 centre; public float radius, faceHalf;
            public Vector4 faceLight, curve, knobs, rimColor, rimShape, grade, gradeGain, haloR, ringPole, ringR, ringE1, ringE2, ringM0, ringM1, ringM2;
            public readonly List<(string key, Vector3 at, float radius)> companions = new List<(string, Vector3, float)>();
        }
        static readonly Dictionary<string, Look> looks = new Dictionary<string, Look>();
        static readonly HashSet<string> missing = new HashSet<string>();
        static Shader sceneShader, backdropShader;

        static Look Get(string key)
        {
            if (string.IsNullOrEmpty(key) || missing.Contains(key)) return null;
            // (all of it still there: the tables made here are objects of the play session that made them, and leaving Play mode
            // destroys them while this cache, kept across sessions without a domain reload, still held them: unlit, haloless)
            if (looks.TryGetValue(key, out var have) && have != null && have.albedo != null && have.face != null && have.corr != null
                && have.curves != null && have.halo != null && have.cover != null) return have;
            var l = Load(key);
            if (l == null) { missing.Add(key); return null; }
            return looks[key] = l;
        }

        /// <summary>A planet map from the converted copy (the HD pack) as install_unity.py imported it: linear data; the
        /// equirect layers round in u and clamped in v; the ring's profile without mipmaps.</summary>
        static Texture2D PackMap(string key, string n)
        {
            string path = $"Resources/{Folder}/{key}/{n}.png";
            bool equirect = n == "albedo" || n == "emission" || n == "night" || n == "glow" || n == "ring";
            if (n == "ring_profile") return Data.OriginalTextures.Data(path, true, false, TextureWrapMode.Clamp, TextureWrapMode.Clamp);
            return Data.OriginalTextures.Data(path, true, true, equirect ? TextureWrapMode.Repeat : TextureWrapMode.Clamp, TextureWrapMode.Clamp, FilterMode.Trilinear);
        }

        static Look Load(string key)
        {
            // the build's own, else the converted copy's (docs/notes/player-assets.md: the HD pack brings them)
            var asset = Resources.Load<TextAsset>($"{Folder}/{key}/planet");
            string text = asset != null ? asset.text : Data.GameContent.Text($"{Folder}/{key}/planet");
            if (text == null) return null;
            JObject j;
            try { j = JObject.Parse(text); }
            catch (System.Exception e) { Debug.LogWarning($"RebuiltPlanets: {key}/planet.json: {e.Message}"); return null; }
            Texture2D Tex(string n) => Resources.Load<Texture2D>($"{Folder}/{key}/{n}") ?? PackMap(key, n);
            var l = new Look { albedo = Tex("albedo"), face = Tex("face"), corr = Tex("corr") };
            if (l.albedo == null || l.face == null || l.corr == null) { Debug.LogWarning($"RebuiltPlanets: {key} is missing a map"); return null; }
            var layers = j["layers"] as JArray;
            bool HasLayer(string n) => layers != null && layers.Exists(n);
            if (HasLayer("emission")) l.emission = Tex("emission");
            if (HasLayer("night")) l.night = Tex("night");
            if (HasLayer("glow")) l.glow = Tex("glow");
            var c = (JArray)j["centre"];
            l.centre = new Vector2((float)c[0], (float)c[1]);
            l.radius = (float)j["radius"];
            l.faceHalf = (float)j["face_half"];
            l.faceLight = V3(j["light"]);
            var lit = (JArray)j["lit"]; var glow = (JArray)j["glow"];
            l.curve = new Vector4((float)j["curve_from"], (float)j["curve_step"], lit.Count, 0f);
            l.knobs = new Vector4((float)j["amb"], (float)j["gain"], (float)j["halo_in"], (float)j["peak"]);
            var rim = V3(j["rim"]);
            l.rimColor = new Vector4(rim.x, rim.y, rim.z, (float)j["rim_knob"]);
            l.rimShape = new Vector4((float)j["rim_power"] * (float)j["rim_pow_knob"], 0f, 0f, 0f);
            // the curves: the light and the glow by band, the grade's level curve over 0..its top
            var curves = new Texture2D(256, 3, TextureFormat.RGBAHalf, false, true) { name = $"rebuilt {key} curves", wrapMode = TextureWrapMode.Clamp, filterMode = FilterMode.Bilinear };
            var px = new Color[256 * 3];
            for (int i = 0; i < 256; i++)
            {
                px[i] = Col(lit[Mathf.Min(i, lit.Count - 1)]);
                px[256 + i] = Col(glow[Mathf.Min(i, glow.Count - 1)]);
            }
            l.grade = new Vector4(1f, 0f, 0f, 0f); l.gradeGain = Vector4.one;
            if (j["grade"] is JObject g)
            {
                var gx = ((JArray)g["x"]).ToObject<float[]>(); var gy = ((JArray)g["y"]).ToObject<float[]>();
                float top = gx[gx.Length - 1];
                for (int i = 0; i < 256; i++) { float y = Interp(i / 255f * top, gx, gy); px[512 + i] = new Color(y, y, y, 1f); }
                l.grade = new Vector4(top, 1f, 0f, 0f);
                var gg = V3(g["gain"]); l.gradeGain = new Vector4(gg.x, gg.y, gg.z, 1f);
            }
            curves.SetPixels(px); curves.Apply(false, true); l.curves = curves;
            // the halo: u the angle from the light's, v the radius
            var h = (JObject)j["halo"]; var hr = (JArray)h["rows"];
            int RB = hr.Count, AB = ((JArray)hr[0]).Count;
            l.haloR = new Vector4((float)h["r_from"], (float)h["r_to"], 0f, 0f);
            var halo = new Texture2D(AB, RB, TextureFormat.RGBAHalf, false, true) { name = $"rebuilt {key} halo", wrapModeU = TextureWrapMode.Repeat, wrapModeV = TextureWrapMode.Clamp, filterMode = FilterMode.Bilinear };
            var hp = new Color[AB * RB];
            for (int ri = 0; ri < RB; ri++) { var row = (JArray)hr[ri]; for (int ai = 0; ai < AB; ai++) hp[ri * AB + ai] = Col(row[ai]); }
            halo.SetPixels(hp); halo.Apply(false, true); l.halo = halo;
            // the edge's cover (its integral out along the radius in the file): u the radius 0.9..1.1, v the angle
            var cv = (JObject)j["cover"]; var cr = (JArray)cv["rows"]; float step = (float)cv["r_step"];
            int NA = cr.Count, NR = ((JArray)cr[0]).Count - 1;
            var cover = new Texture2D(NR, NA, TextureFormat.RHalf, false, true) { name = $"rebuilt {key} cover", wrapModeU = TextureWrapMode.Clamp, wrapModeV = TextureWrapMode.Repeat, filterMode = FilterMode.Bilinear };
            var cp = new Color[NR * NA];
            for (int a = 0; a < NA; a++)
            {
                var row = ((JArray)cr[a]).ToObject<float[]>();
                for (int k = 0; k < NR; k++) { float v = Mathf.Clamp01((row[k + 1] - row[k]) / step); cp[a * NR + k] = new Color(v, v, v, 1f); }
            }
            cover.SetPixels(cp); cover.Apply(false, true); l.cover = cover;
            if (j["ring"] is JObject rg)   // (the ring as its png shows it: its texture round it, the png's camera, its plane in 3D)
            {
                l.ring = Tex("ring");
                if (l.ring != null)
                {
                    var pole = V3(rg["pole"]);
                    float outer = (float)rg["outer_body_r"], to = (float)rg["r_to"];
                    l.ringPole = new Vector4(pole.x, pole.y, pole.z, 1f);
                    // (the quad out to where the png's ring reaches, or the 3D ring's edge seen face on)
                    // (the 3D ring: round, its band profile from its inner edge out; the backdrop's: the png's own through its camera)
                    l.ringProfile = Tex("ring_profile");
                    if (l.ringProfile != null) l.ringProfile.wrapMode = TextureWrapMode.Clamp;
                    float pTo = (float)rg["profile_to"];
                    l.ringProfileR = new Vector4((float)rg["profile_from"], pTo, 0f, 0f);
                    // (the quad out to where the png's ring reaches, or the 3D ring's edge seen face on)
                    l.ringR = new Vector4((float)rg["r_from"], to, Mathf.Max((float)rg["reach"], Mathf.Max(to * outer, pTo) + 0.05f), outer);
                    l.ringE1 = new Vector4(0f, 0f, 0f, (float)rg["near"]);
                    l.ringE2 = Vector4.zero;
                    var mm = (JArray)rg["png_to_plane"];
                    l.ringM0 = Row(mm[0]); l.ringM1 = Row(mm[1]); l.ringM2 = Row(mm[2]);
                }
            }
            if (j["companions"] is JArray comps)
                foreach (JObject co in comps) l.companions.Add(((string)co["key"], V3(co["offset"]), (float)co["radius"]));
            return l;
        }

        static Vector3 V3(JToken t) { var a = (JArray)t; return new Vector3((float)a[0], (float)a[1], (float)a[2]); }
        static Vector4 Row(JToken t) { var a = (JArray)t; return new Vector4((float)a[0], (float)a[1], (float)a[2], 0f); }
        static Color Col(JToken t) { var a = (JArray)t; return new Color((float)a[0], (float)a[1], (float)a[2], 1f); }
        static float Interp(float x, float[] xs, float[] ys)
        {
            if (x <= xs[0]) return ys[0];
            for (int i = 1; i < xs.Length; i++)
                if (x <= xs[i]) { float d = xs[i] - xs[i - 1]; return d > 1e-9f ? Mathf.Lerp(ys[i - 1], ys[i], (x - xs[i - 1]) / d) : ys[i]; }
            return ys[ys.Length - 1];
        }
        static bool Exists(this JArray a, string v) { foreach (var t in a) if ((string)t == v) return true; return false; }
    }
}
