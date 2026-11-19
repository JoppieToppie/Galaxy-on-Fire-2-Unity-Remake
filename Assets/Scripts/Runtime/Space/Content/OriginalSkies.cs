// OriginalSkies.cs
// Feature: docs/notes/player-assets.md "The content layer": the skies and reflection cubemaps of a build without the
// originals, made from the player's converted copy when Resources has none:
//   Sky("stars_000" / "nebula_005")  what SkyboxBaker.BakeSpaceSkies baked into Resources/GoF2Sky: the original's sky meshes
//                                    with their textures (GoF2/Unlit stars, GoF2/Additive nebula, seen from inside) rendered
//                                    as the baker renders them, compressed and kept on disk (rendered once, ever)
//   Reflection("cubemap_skybox_003") the original's own cubemap strip (AEI type 0x81, 6 faces stacked, the converter's
//                                    Textures/<pack>/cubemaps/<name>.png) as a Cubemap: Resources/GoF2Reflections holds 4x
//                                    upscales of the same (an optional upgrade, see the packs)

using System;
using System.Collections.Generic;
using System.IO;
using GoF2Remake.Data;
using UnityEngine;
using UnityEngine.Rendering;

namespace GoF2Remake.Visuals
{
    [Unity.Scripting.LifecycleManagement.NoAutoStaticsCleanup]
    public static class OriginalSkies
    {
        static readonly Dictionary<string, Texture> made = new Dictionary<string, Texture>();

        [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.SubsystemRegistration)]
        static void ResetStatics()
        {
            made.Clear();
            skyboxes.Clear();
            GameContent.Refreshed -= ResetStatics;
            GameContent.Refreshed += ResetStatics;   // (a new copy converted)
        }

        /// <summary>The face size of a rendered sky (SkyboxBaker's 2048; phones 1024, its strips are 8192 there).</summary>
        public static int FaceSize => Application.isMobilePlatform ? 1024 : 2048;

        /// <summary>A space sky layer by its GoF2Sky name: Resources' cubemap, else rendered from the converted copy.</summary>
        public static Texture Sky(string name)
        {
            var c = Resources.Load<Cubemap>("GoF2Sky/" + name);
            if (c != null) return c;
            if (made.TryGetValue("sky:" + name, out var t) && t != null) return t;
            t = Render(name);
            made["sky:" + name] = t;
            return t;
        }

        /// <summary>The meshes and textures of a sky (SkyboxBaker: stars_i the stars mesh, nebula_t its system's; combined_i
        /// the old combined bake, Bake(i): stars_(i % 3) under nebula_i, the story intros' skyboxes).</summary>
        static (string model, string shader, string texture)[] LayersOf(string name)
        {
            if (name.StartsWith("combined_") && int.TryParse(name.Substring(9), out int c))
            {
                var stars = LayersOf($"stars_{c % 3:000}");
                var nebula = LayersOf($"nebula_{c:000}");
                return stars != null && nebula != null ? new[] { stars[0], nebula[0] } : null;
            }
            if (name.StartsWith("stars_") && int.TryParse(name.Substring(6), out int i))
                return new[] { ("Models/main/skyboxes/skybox_stars.fbx", "GoF2/Unlit", $"Textures/main/skyboxes/skybox_stars_{i:000}.png") };
            if (!name.StartsWith("nebula_") || !int.TryParse(name.Substring(7), out int t)) return null;
            if (t <= 10) return new[] { ($"Models/main/skyboxes/skybox_{t:000}.fbx", "GoF2/Additive", $"Textures/main/skyboxes/skybox_{t:000}.png") };
            if (t <= 14) return new[] { ($"Models/valkyrie/skyboxes/v_skybox_{(t == 14 ? 13 : t):000}.fbx", "GoF2/Additive", $"Textures/valkyrie/skyboxes/v_skybox_{t:000}.png") };
            return new[] { ($"Models/supernova/skyboxes/sn_skybox_{t:000}.fbx", "GoF2/Additive", $"Textures/supernova/skyboxes/sn_skybox_{t:000}.png") };
        }

        /// <summary>A skybox material of the old combined bakes (Skyboxes/skybox_NNN.mat, SkyboxBaker.Bake: Skybox/Cubemap,
        /// exposure 1): the story intros' skies. Null without a GPU or the copy.</summary>
        public static Material Skybox(int index)
        {
            string key = $"skybox:{index}";
            if (skyboxes.TryGetValue(key, out var have)) return have;
            var cube = Sky($"combined_{index:000}");
            var shader = Shader.Find("Skybox/Cubemap");
            Material m = null;
            if (cube != null && shader != null)
            {
                m = new Material(shader) { name = $"skybox_{index:000}" };
                m.SetTexture("_Tex", cube);
                m.SetFloat("_Exposure", 1f);
            }
            skyboxes[key] = m;
            return m;
        }

        static readonly Dictionary<string, Material> skyboxes = new Dictionary<string, Material>();

        static readonly Vector3[] FaceForward = { Vector3.right, Vector3.left, Vector3.up, Vector3.down, Vector3.forward, Vector3.back };
        static readonly Vector3[] FaceUp = { Vector3.up, Vector3.up, Vector3.back, Vector3.forward, Vector3.up, Vector3.up };

        /// <summary>A sky layer as a compressed Cubemap: from the GPU cache (GameContent.GpuCache), else rendered as
        /// SkyboxBaker.BakeCube renders it (a camera per face, 90 degrees, black behind, the faces read back into a 6:1
        /// strip), made a cubemap as Unity's import makes one of that strip (FromStrip's faces), each face compressed with its
        /// mipmaps (ModTextureEncoder) and stored. Null without a GPU or the layer's model / texture.</summary>
        static Texture Render(string name)
        {
            var layers = LayersOf(name);
            // (no GPU: a server or a -nographics batch run renders nothing)
            if (layers == null || !GameContent.Installed || SystemInfo.graphicsDeviceType == GraphicsDeviceType.Null) return null;
            // (rendered from the HD folder's textures while it is on: a sky of its own)
            string cacheFile = Path.Combine(GameContent.GpuCache, "skies", $"{name}_{FaceSize}{(GameContent.HdActive ? "_hd" : "")}_{SystemInfo.graphicsDeviceType}.sky");
            using var timing = ContentTimings.Measure("sky rendered");
            var cached = ReadCube(cacheFile, name, FaceSize);
            if (cached != null) { timing.Kind = "sky from GPU cache"; return cached; }
            var strip = RenderStrip(layers);
            if (strip == null) return null;
            var cube = CompressedCube(strip, name, cacheFile);
            OriginalAssemblies.Gone(strip);
            return cube;
        }

        /// <summary>SkyboxBaker.BakeCube's strip: six 90-degree renders of the layers seen from their centre, read back.</summary>
        static Texture2D RenderStrip((string model, string shader, string texture)[] layers)
        {
            const int Layer = 31;
            var origin = Vector3.zero;   // on a layer of its own: only this camera sees it
            var gos = new List<GameObject>();
            var mats = new List<Material>();
            void Drop() { foreach (var g in gos) { g.SetActive(false); OriginalAssemblies.Gone(g); } foreach (var m in mats) OriginalAssemblies.Gone(m); }
            foreach (var layer in layers)
            {
                var go = OriginalModels.Build(layer.model);
                var shader = Shader.Find(layer.shader);
                var tex = OriginalTextures.Get(layer.texture);
                if (go != null) gos.Add(go);
                if (go == null || shader == null || tex == null) { Drop(); return null; }
                go.transform.position = origin;
                var mat = new Material(shader);
                mats.Add(mat);
                mat.SetTexture("_MainTex", tex);
                mat.SetFloat("_Cull", 0f);   // seen from inside
                foreach (var r in go.GetComponentsInChildren<MeshRenderer>(true))
                {
                    r.sharedMaterial = mat;
                    r.shadowCastingMode = ShadowCastingMode.Off;
                    r.gameObject.layer = Layer;
                }
                var anim = go.GetComponent<PartAnimation>();
                if (anim != null) anim.enabled = false;
            }
            int s = FaceSize;
            var rt = new RenderTexture(s, s, 24, RenderTextureFormat.ARGB32);
            var strip = new Texture2D(s * 6, s, TextureFormat.RGB24, false);
            var camGo = new GameObject("Sky render camera");
            var cam = camGo.AddComponent<Camera>();
            cam.enabled = false;
            cam.clearFlags = CameraClearFlags.SolidColor;
            cam.backgroundColor = Color.black;
            cam.cullingMask = 1 << Layer;
            cam.fieldOfView = 90f;
            cam.aspect = 1f;
            cam.nearClipPlane = 0.1f;
            cam.farClipPlane = 1000f;
            cam.targetTexture = rt;
            var data = camGo.AddComponent<UnityEngine.Rendering.Universal.UniversalAdditionalCameraData>();
            data.renderPostProcessing = false;
            camGo.transform.position = origin;
            var prev = RenderTexture.active;
            for (int f = 0; f < 6; f++)
            {
                camGo.transform.rotation = Quaternion.LookRotation(FaceForward[f], FaceUp[f]);
                cam.Render();
                RenderTexture.active = rt;
                strip.ReadPixels(new Rect(0, 0, s, s), f * s, 0);
            }
            RenderTexture.active = prev;
            strip.Apply();
            cam.targetTexture = null;
            rt.Release();
            OriginalAssemblies.Gone(rt);
            OriginalAssemblies.Gone(camGo);
            Drop();   // (inactive first: Destroy waits for the frame's end, and no other camera draws them meanwhile)
            return strip;
        }

        const int SkyMagic = 0x31594B53;   // "SKY1"

        /// <summary>The strip as a compressed cubemap (DXT1 / ETC2 faces with mipmaps, copied into the cube on the GPU),
        /// stored in 'cacheFile'; uncompressed where the GPU can't copy between textures or reads neither format.</summary>
        static Texture CompressedCube(Texture2D strip, string name, string cacheFile)
        {
            int size = strip.height;   // (a 6:1 strip: the rendered skies' FaceSize, the HD pack's 2048)
            bool dxt = SystemInfo.SupportsTextureFormat(TextureFormat.DXT1);
            bool etc = !dxt && SystemInfo.SupportsTextureFormat(TextureFormat.ETC2_RGB);
            if ((!dxt && !etc) || SystemInfo.copyTextureSupport == CopyTextureSupport.None || size % 4 != 0)
                return FromStrip(strip, name);
            var target = etc ? Modding.ModTextureEncoder.Target.Etc2 : Modding.ModTextureEncoder.Target.Dxt;
            var faces = new byte[6][];
            Cubemap cube = null;
            for (int i = 0; i < 6; i++)
            {
                // the face in a cubemap face's row order (FromStrip's), as a texture whose raw rows are in that order: the GPU
                // copy below moves rows as they are
                var face = new Texture2D(size, size, TextureFormat.RGBA32, false, false);
                face.SetPixels(Face(strip, i, true));
                face.Apply(false, false);
                var work = Modding.ModTextureEncoder.Start(face, false, Modding.ModTextureEncoder.Remap.None, 1f, 1f, target);
                OriginalAssemblies.Gone(face);
                if (work == null) { if (cube != null) OriginalAssemblies.Gone(cube); return FromStrip(strip, name); }
                var t = work.Upload(name);
                faces[i] = work.Data.ToArray();
                work.Dispose();
                cube ??= NewCube(name, t.format, t.mipmapCount, size);
                Graphics.CopyTexture(t, 0, cube, i);
                OriginalAssemblies.Gone(t);
            }
            WriteCube(cacheFile, cube.format, cube.mipmapCount, size, faces);
            return cube;
        }

        static Cubemap NewCube(string name, TextureFormat format, int mips, int size)
        {
            var gf = UnityEngine.Experimental.Rendering.GraphicsFormatUtility.GetGraphicsFormat(format, true);
            var cube = new Cubemap(size, gf, UnityEngine.Experimental.Rendering.TextureCreationFlags.MipChain, mips)
                { name = name, wrapMode = TextureWrapMode.Clamp, filterMode = FilterMode.Trilinear };
            cube.Apply(false, true);   // GPU only: the faces are copied in
            return cube;
        }

        static void WriteCube(string file, TextureFormat format, int mips, int size, byte[][] faces)
        {
            try
            {
                Directory.CreateDirectory(Path.GetDirectoryName(file));
                string tmp = file + ".tmp";
                using (var w = new BinaryWriter(File.Create(tmp)))
                {
                    w.Write(SkyMagic); w.Write(OriginalConverter.FormatVersion); w.Write(size); w.Write((int)format); w.Write(mips);
                    foreach (var f in faces) { w.Write(f.Length); w.Write(f); }
                }
                if (File.Exists(file)) File.Delete(file);
                File.Move(tmp, file);
            }
            catch (IOException e) { Debug.LogWarning($"OriginalSkies: {file}: {e.Message}"); }
        }

        /// <summary>A stored cubemap ('size' its faces', or -1: whatever the file has).</summary>
        static Cubemap ReadCube(string file, string name, int size)
        {
            if (!File.Exists(file) || SystemInfo.copyTextureSupport == CopyTextureSupport.None) return null;
            try
            {
                using var r = new BinaryReader(File.OpenRead(file));
                if (r.ReadInt32() != SkyMagic || r.ReadInt32() != OriginalConverter.FormatVersion) return null;
                int stored = r.ReadInt32();
                if (size > 0 && stored != size) return null;
                size = stored;
                var format = (TextureFormat)r.ReadInt32();
                int mips = r.ReadInt32();
                if (!SystemInfo.SupportsTextureFormat(format)) return null;
                var cube = NewCube(name, format, mips, size);
                for (int i = 0; i < 6; i++)
                {
                    var bytes = r.ReadBytes(r.ReadInt32());
                    var t = new Texture2D(size, size, format, mips, false);
                    t.LoadRawTextureData(bytes);
                    t.Apply(false, true);
                    Graphics.CopyTexture(t, 0, cube, i);
                    OriginalAssemblies.Gone(t);
                }
                return cube;
            }
            catch (Exception e) when (e is IOException || e is EndOfStreamException || e is UnityException || e is ArgumentException)
            {
                Debug.LogWarning($"OriginalSkies: {file}: {e.Message}");
                return null;
            }
        }

        /// <summary>An Enhanced skies' cubemap by its GoF2SkyHd name ("stars_000", "nebula_005"): Resources', else the HD pack's
        /// strip in the converted copy (Resources/GoF2SkyHd/<name>.png, 6:1, sRGB), made a compressed cubemap once and kept
        /// in the GPU cache like a rendered sky. Null when neither has it.</summary>
        public static Cubemap Hd(string name)
        {
            var c = Resources.Load<Cubemap>("GoF2SkyHd/" + name);
            if (c != null) return c;
            if (made.TryGetValue("hd:" + name, out var t) && t != null) return t as Cubemap;
            using var timing = ContentTimings.Measure("HD sky");
            string png = GameContent.CachePath($"Resources/GoF2SkyHd/{name}.png");
            if (png == null || SystemInfo.graphicsDeviceType == GraphicsDeviceType.Null) return null;
            var fi = new FileInfo(png);
            string cacheFile = Path.Combine(GameContent.GpuCache, "skies", $"hd_{name}_{fi.Length}_{SystemInfo.graphicsDeviceType}.sky");
            var cube = ReadCube(cacheFile, name, -1);
            if (cube == null)
            {
                var strip = new Texture2D(2, 2, TextureFormat.RGBA32, false, false);
                if (!strip.LoadImage(File.ReadAllBytes(png), false)) { OriginalAssemblies.Gone(strip); return null; }
                cube = CompressedCube(strip, name, cacheFile) as Cubemap;
                OriginalAssemblies.Gone(strip);
            }
            made["hd:" + name] = cube;
            return cube;
        }

        /// <summary>A reflection cubemap by its GoF2Reflections name: Resources' (the 4x upscales), else the original's own
        /// strip from the converted copy. Null when neither has it.</summary>
        public static Cubemap Reflection(string name)
        {
            var c = Resources.Load<Cubemap>("GoF2Reflections/" + name);
            if (c != null) return c;
            if (made.TryGetValue("refl:" + name, out var t)) return t as Cubemap;
            string path = null;
            foreach (var pack in new[] { "main", "valkyrie", "supernova" })
                if ((path = GameContent.CachePath($"Textures/{pack}/cubemaps/{name}.png")) != null) break;
            if (path != null) c = FromStrip(File.ReadAllBytes(path), name);   // (sRGB colour, mipmapped)
            made["refl:" + name] = c;
            return c;
        }

        static readonly CubemapFace[] Faces =
            { CubemapFace.PositiveX, CubemapFace.NegativeX, CubemapFace.PositiveY, CubemapFace.NegativeY, CubemapFace.PositiveZ, CubemapFace.NegativeZ };

        /// <summary>A strip of six faces (+X, -X, +Y, -Y, +Z, -Z: left to right in a 6:1 image, top to bottom in a 1:6 one, as
        /// Unity's cubemap import reads them) as a Cubemap the way that import makes it (CubeStripTests).</summary>
        public static Cubemap FromStrip(byte[] png, string name, bool linear = false, bool mipmaps = true)
        {
            var strip = new Texture2D(2, 2, TextureFormat.RGBA32, false, linear);
            if (!strip.LoadImage(png, false)) { OriginalAssemblies.Gone(strip); return null; }
            var cube = FromStrip(strip, name, linear, mipmaps);
            OriginalAssemblies.Gone(strip);
            return cube;
        }

        public static Cubemap FromStrip(Texture2D strip, string name, bool linear = false, bool mipmaps = true)
        {
            bool horizontal = strip.width == strip.height * 6;
            if (!horizontal && strip.height != strip.width * 6) return null;
            int s = horizontal ? strip.height : strip.width;
            var cube = new Cubemap(s, TextureFormat.RGBA32, mipmaps) { name = name, wrapMode = TextureWrapMode.Clamp, filterMode = FilterMode.Trilinear };
            for (int i = 0; i < 6; i++) cube.SetPixels(Face(strip, i, horizontal), Faces[i]);
            cube.Apply(mipmaps, false);
            return cube;
        }

        /// <summary>Face i of the strip in a cubemap face's pixel order (rows from the top: a Texture2D's from the bottom).</summary>
        static Color[] Face(Texture2D strip, int i, bool horizontal)
        {
            int s = horizontal ? strip.height : strip.width;
            var px = horizontal ? strip.GetPixels(i * s, 0, s, s) : strip.GetPixels(0, (5 - i) * s, s, s);
            var o = new Color[px.Length];
            for (int y = 0; y < s; y++) System.Array.Copy(px, y * s, o, (s - 1 - y) * s, s);
            return o;
        }
    }
}
