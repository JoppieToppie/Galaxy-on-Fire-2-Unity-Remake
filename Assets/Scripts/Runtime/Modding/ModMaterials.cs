// ModMaterials.cs
// Materials for mods' ships, always copies of the URP Lit templates in ModAssets:
//   - FromGltf: the glTF model's own PBR materials (base colour, metallic-roughness converted to URP's metallic /
//     smoothness map, normal map, emission, alpha mode), through ModGltfMaterials (glTFast's material generator);
//   - FromSpec: a ships.json "materials" entry (CustomShipMaterial, the PR #34 format): the mod's PNG textures as diffuse,
//     normal, metallic (R) / smoothness (A) mask, emission, detail maps, alpha clip or glass; it replaces the model's
//     material on the renderers / submeshes it names.
// Textures load once per mod and file (Texture), compressed, with mipmaps. ModShips decodes them first in the background
// (PreloadTexture: UnityWebRequestTexture decodes the PNG / JPG off the main thread, ImageImport a TGA / BMP / DDS / .aei
// on a worker thread, ModTextureEncoder compresses it to
// DXT with its mipmaps on a worker thread where the GPU reads DXT; elsewhere Texture2D.Compress, one texture per frame), so
// the builder's Texture calls find them made. The result is kept on disk (ModTextureCache): a later start reads it back on
// a worker thread instead of decoding and compressing again. A GLB's embedded images come the same way (PreloadImage, by
// their content, see ModGltf), their role's conversion done on the CPU before compression. A texture is made once however
// many ask for it at the same time (Once), and at most DecodeSlots are decoded at once (each is uncompressed until then).
// Normal maps (normal = true): Android builds read a normal map's X from its alpha channel and Y from green (Player settings,
// Android normal map encoding "DXT5nm-style": URP's UnpackNormal with UNITY_ASTC_NORMALMAP_ENCODING). The game's normal maps
// are imported as Normal Map textures, which Unity re-encodes that way; a mod's PNG arrives as plain RGB with alpha 1, so on
// Android every normal came out bent sideways (X = 1) and the mods' hulls nearly black. On Android the loader copies X (red)
// into alpha before compressing (NormalsInAlpha); desktop reads either layout (UnpackNormalmapRGorAG) and is left as it was.

using System.Collections.Generic;
using System.Threading;
using System.Threading.Tasks;
using GoF2Remake.Data;
using UnityEngine;
using UnityEngine.Rendering;

namespace GoF2Remake.Modding
{
    [Unity.Scripting.LifecycleManagement.NoAutoStaticsCleanup]
    public static class ModMaterials
    {
        static readonly Dictionary<string, Texture2D> textures = new Dictionary<string, Texture2D>();
        static readonly Dictionary<(Color32, bool), Texture2D> solids = new Dictionary<(Color32, bool), Texture2D>();
        static readonly List<Object> made = new List<Object>();

        /// <summary>The override tag on a material whose emission map is its "damageEmission" mask (DamageEmbers drives only
        /// those; a material with an emission of its own keeps it, its charring still follows the damage).</summary>
        public const string DamageEmbersTag = "GoF2DamageEmbers";

        static int revision = -1;

        [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.SubsystemRegistration)]
        static void ResetStatics() { textures.Clear(); solids.Clear(); made.Clear(); revision = -1; }

        /// <summary>The mods changed: what was made for the old ones goes (ships, stations and backdrops share this cache, so it
        /// isn't one loader's to clear).</summary>
        static void Check()
        {
            if (revision == ModManager.Revision) return;
            Release();
            revision = ModManager.Revision;
        }

        /// <summary>Everything made for the mods' ships goes (the mods changed).</summary>
        public static void Release()
        {
            foreach (var o in made) if (o != null) Gone(o);
            made.Clear();
            textures.Clear();
            solids.Clear();
            pending.Clear();   // what is still loading is dropped when it ends (Store: the revision changed)
        }

        // (edit mode too: the Editor's tests and tools make mod materials, where Destroy isn't allowed)
        static void Gone(Object o) { if (Application.isPlaying) Object.Destroy(o); else Object.DestroyImmediate(o); }

        /// <summary>The build reads normal maps' X from alpha (Android's normal map encoding, see the header).</summary>
        public static bool NormalsInAlpha =>
#if UNITY_ANDROID
            true;
#else
            false;
#endif

        static string Key(ModInfo mod, string path, bool linear, bool readable, bool normal) =>
            mod.Id + "|" + ModSource.Normalise(path).ToLowerInvariant() + (linear ? "|l" : "") + (readable ? "|r" : "")
            + (normal && NormalsInAlpha ? "|n" : "");

        /// <summary>How a decoded texture is converted before it is compressed.</summary>
        enum Remap { None, NormalToAlpha, MetallicRoughness }

        /// <summary>A normal map in the Android layout: X (red) copied into alpha, the mipmaps made again (RGBA32 if it had no
        /// alpha). Returns the texture to use (a new one when the format changed; the old one is destroyed).</summary>
        static Texture2D NormalToAlpha(Texture2D t) => Convert(t, Remap.NormalToAlpha);

        /// <summary>A decoded (readable) texture converted for its use: NormalToAlpha above, or a glTF metallic-roughness map
        /// (B metallic, G roughness) as URP Lit's metallic / smoothness map (R metallic, A smoothness = 1 - roughness), like
        /// Hidden/GoF2/MetallicRoughnessToGloss but before compression (the shader's render texture stayed uncompressed).
        /// The material's factors are baked in (glTF: metallic = metallicFactor x B, roughness = roughnessFactor x G): URP Lit
        /// has no metallic multiplier for its map, and the GoF3 ships' maps are all metal in B with metallicFactor 0, which
        /// taken as 1 made every hull a mirror, black against space.</summary>
        static Texture2D Convert(Texture2D t, Remap remap, float metal = 1f, float rough = 1f)
        {
            var px = t.GetPixels32();
            if (remap == Remap.NormalToAlpha) for (int i = 0; i < px.Length; i++) px[i].a = px[i].r;
            else
                for (int i = 0; i < px.Length; i++)
                    px[i] = new Color32((byte)(px[i].b * metal + 0.5f), 0, 0, (byte)(255 - (int)(px[i].g * rough + 0.5f)));
            bool mips = t.mipmapCount > 1;
            var o = t;
            if (t.format != TextureFormat.RGBA32 && t.format != TextureFormat.ARGB32)
            {
                o = new Texture2D(t.width, t.height, TextureFormat.RGBA32, mips, true)
                    { name = t.name, wrapMode = t.wrapMode, anisoLevel = t.anisoLevel, filterMode = t.filterMode };
                Object.Destroy(t);
            }
            o.SetPixels32(px);
            o.Apply(mips, false);
            return o;
        }

        static Material normalToAlpha;

        /// <summary>A glTF normal map (glTFast's texture, not readable) in the Android layout, on the GPU.</summary>
        static Texture NormalToAlpha(Texture src, string name)
        {
            if (normalToAlpha == null)
            {
                var shader = Resources.Load<Shader>("GoF2Mods/NormalToAlpha");
                if (shader == null) return src;
                normalToAlpha = new Material(shader);
            }
            var rt = new RenderTexture(src.width, src.height, 0, RenderTextureFormat.ARGB32, RenderTextureReadWrite.Linear)
                { name = name + " normal", useMipMap = true, autoGenerateMips = true, wrapMode = src.wrapMode, anisoLevel = 8 };
            Graphics.Blit(src, rt, normalToAlpha);
            made.Add(rt);
            return rt;
        }

        /// <summary>Whether a texture of the mod is loaded (Texture returns it without reading the file; also true for a file
        /// that failed, which Texture answers with null).</summary>
        public static bool Has(ModInfo mod, string path, bool linear, bool readable = false, bool normal = false)
        {
            Check();
            return mod == null || string.IsNullOrEmpty(path) || textures.ContainsKey(Key(mod, path, linear, readable, normal));
        }

        /// <summary>A PNG / JPG of the mod (null = none or unreadable); linear for data (normal, metallic, masks); normal for a
        /// normal map (its Android layout).</summary>
        public static Texture2D Texture(ModInfo mod, string path, bool linear, bool readable = false, bool normal = false)
        {
            Check();
            if (mod == null || string.IsNullOrEmpty(path)) return null;
            string key = Key(mod, path, linear, readable, normal);
            if (textures.TryGetValue(key, out var t)) return t;
            byte[] bytes;
            try { bytes = mod.Source.ReadBytes(path); }
            catch (System.Exception e) when (ModFileException.IsFileError(e)) { Warn(mod, e.Message); textures[key] = null; return null; }
            if (bytes == null) { Warn(mod, $"{path}: file not found"); textures[key] = null; return null; }
            // PNG / JPG by Unity's decoder, TGA / BMP / DDS / the game's .aei by ImageImport.
            t = ImageImport.Decode(bytes, path, linear, true, out string decodeError);
            if (t == null) { Warn(mod, $"{path}: {decodeError}"); textures[key] = null; return null; }
            t.name = mod.Id + ":" + path;
            t.wrapMode = TextureWrapMode.Repeat;
            if (normal && NormalsInAlpha) t = NormalToAlpha(t);
            t.anisoLevel = 8;
            t.filterMode = FilterMode.Trilinear;
            if (!readable)
            {
                if (t.width % 4 == 0 && t.height % 4 == 0) t.Compress(true);
                t.Apply(false, true);
            }
            made.Add(t);
            textures[key] = t;
            return t;
        }

        static int compressFrame = -1;
        static readonly HashSet<string> loggedFormats = new HashSet<string>();
        static readonly Dictionary<string, Task> pending = new Dictionary<string, Task>();
        static SemaphoreSlim decodeSlots;

        /// <summary>Decodes at once at most: each decoded texture is uncompressed (a 1024 px one 4-5.6 MB) until it is
        /// compressed, and a mod of many models asks for hundreds at the same time.</summary>
        static SemaphoreSlim DecodeSlots => decodeSlots ??= new SemaphoreSlim(Application.isMobilePlatform ? 2 : Mathf.Clamp(SystemInfo.processorCount / 2, 4, 12));

        static SemaphoreSlim cacheSlots;

        /// <summary>Cached textures read at once (their compressed data held until uploaded).</summary>
        static SemaphoreSlim CacheSlots => cacheSlots ??= new SemaphoreSlim(Application.isMobilePlatform ? 4 : 16);

        /// <summary>Decodes a texture in the background into the cache Texture reads (nothing when it is there already, or
        /// when the background way fails: Texture then loads it the plain way).</summary>
        public static Task PreloadTexture(ModInfo mod, string path, bool linear, bool readable = false, bool normal = false)
        {
            Check();
            if (mod == null || string.IsNullOrEmpty(path)) return Task.CompletedTask;
            string key = Key(mod, path, linear, readable, normal);
            var remap = normal && NormalsInAlpha ? Remap.NormalToAlpha : Remap.None;
            return Once(key, () => Decode(key, mod.Id + ":" + path, mod.Id, path, null, () => mod.LocalFile(path), linear, readable, remap));
        }

        /// <summary>A role's key part: "MetallicRoughness" alone for the factors (1, 1), so textures cached before the factors
        /// counted stay valid for them; else the factors too.</summary>
        static string RoleKey(ModGlbImages.Role role, float metal, float rough) =>
            role != ModGlbImages.Role.MetallicRoughness || (metal >= 1f && rough >= 1f) ? role.ToString()
            : role + "@" + metal.ToString("0.###", System.Globalization.CultureInfo.InvariantCulture)
                   + "," + rough.ToString("0.###", System.Globalization.CultureInfo.InvariantCulture);

        static string ImageKey(ModInfo mod, string hash, ModGlbImages.Role role, float metal, float rough) =>
            mod.Id + "|#image:" + hash + "|" + RoleKey(role, metal, rough) + (role == ModGlbImages.Role.Normal && NormalsInAlpha ? "|n" : "");

        /// <summary>A GLB's embedded image (ModGltf, ModGlbImages) as a texture for one role, made in the background once per
        /// mod and image content: base colour / emission sRGB, normal and metallic-roughness maps linear and converted (a
        /// metallic-roughness map once per pair of its materials' metallic / roughness factors).</summary>
        public static Task PreloadImage(ModInfo mod, string hash, string ext, byte[] bytes, ModGlbImages.Role role,
            float metal = 1f, float rough = 1f)
        {
            Check();
            string key = ImageKey(mod, hash, role, metal, rough);
            string roleKey = RoleKey(role, metal, rough);
            bool linear = role != ModGlbImages.Role.Color;
            var remap = role == ModGlbImages.Role.MetallicRoughness ? Remap.MetallicRoughness
                      : role == ModGlbImages.Role.Normal && NormalsInAlpha ? Remap.NormalToAlpha : Remap.None;
            return Once(key, () => Decode(key, $"{mod.Id}:{hash.Substring(0, 8)} {roleKey}", mod.Id, null, hash + "|" + roleKey,
                () => mod.LocalImage(hash + "_" + role, ext, bytes), linear, false, remap, metal, rough));
        }

        /// <summary>A texture PreloadImage made (null: none, or it couldn't be read).</summary>
        public static Texture2D Image(ModInfo mod, string hash, ModGlbImages.Role role, float metal = 1f, float rough = 1f)
        {
            Check();
            return textures.TryGetValue(ImageKey(mod, hash, role, metal, rough), out var t) ? t : null;
        }

        /// <summary>One load per key: a key already made or being made isn't made again (many models share images).</summary>
        static Task Once(string key, System.Func<Task> make)
        {
            if (textures.ContainsKey(key)) return Task.CompletedTask;
            if (pending.TryGetValue(key, out var running)) return running;
            var task = Run();
            if (!task.IsCompleted) pending[key] = task;
            return task;

            async Task Run()
            {
                try { await make(); }
                finally { pending.Remove(key); }
            }
        }

        /// <summary>Stores a made texture, unless the mods changed meanwhile (then it goes).</summary>
        static void Store(string key, Texture2D t, int rev)
        {
            if (rev != revision || textures.ContainsKey(key)) { Object.Destroy(t); return; }
            t.wrapMode = TextureWrapMode.Repeat;
            t.anisoLevel = 8;
            t.filterMode = FilterMode.Trilinear;
            made.Add(t);
            textures[key] = t;
        }

        /// <summary>Reads a texture back from the disk cache or decodes, converts and compresses it. The cache file is named
        /// by the mod file's path, size and date ('path'), or by the content ('content', a GLB's image). A cached texture
        /// needs no decode slot (its data is compressed already): many are read at once (CacheSlots), only the decodes wait
        /// for DecodeSlots.</summary>
        static async Task Decode(string key, string name, string modId, string path, string content, System.Func<string> localFile,
            bool linear, bool readable, Remap remap, float metal = 1f, float rough = 1f)
        {
            int rev = revision;
            // Desktop GPUs read DXT, phones ETC2: the worker threads compress it with its own mipmaps (ModTextureEncoder);
            // Texture2D.Compress on the main thread only for a GPU that reads neither.
            bool dxt = !readable && SystemInfo.SupportsTextureFormat(TextureFormat.DXT1) && SystemInfo.SupportsTextureFormat(TextureFormat.DXT5);
            bool etc = !readable && !dxt && SystemInfo.SupportsTextureFormat(TextureFormat.ETC2_RGB) && SystemInfo.SupportsTextureFormat(TextureFormat.ETC2_RGBA8);
            bool encode = dxt || etc;
            var target = etc ? ModTextureEncoder.Target.Etc2 : ModTextureEncoder.Target.Dxt;
            string kind = (dxt ? "dxt" : etc ? "etc2" : "gpu") + (remap == Remap.NormalToAlpha ? "-nrm" : remap == Remap.MetallicRoughness ? "-mr" : "");
            string cacheRoot = ModTextureCache.Root;   // persistentDataPath: main thread only
            string file = null, cacheFile = null;
            if (!readable)
            {
                // Made before (ModTextureCache): read back off the main thread.
                ModTextureCache.Entry cached = null;
                var cacheSlots = CacheSlots;
                await cacheSlots.WaitAsync();
                try
                {
                    file = await Task.Run(() =>
                    {
                        string f = null;
                        if (content != null) cacheFile = ModTextureCache.FileForContent(cacheRoot, modId, content, linear, kind);
                        else if ((f = localFile()) != null) cacheFile = ModTextureCache.FileFor(cacheRoot, modId, path, f, linear, kind);
                        if (cacheFile != null) cached = ModTextureCache.Read(cacheFile);
                        return f;
                    });
                }
                finally { cacheSlots.Release(); }
                if (rev != revision || textures.ContainsKey(key)) { cached?.Dispose(); return; }
                if (cached != null)
                {
                    await UploadTurn();
                    if (rev != revision || textures.ContainsKey(key)) { cached.Dispose(); return; }
                    long upload = System.Diagnostics.Stopwatch.GetTimestamp();
                    var fromCache = ModTextureCache.Make(cached, name);
                    Uploaded(upload);
                    if (fromCache != null) { Store(key, fromCache, rev); return; }
                }
            }
            var slots = DecodeSlots;
            await slots.WaitAsync();
            try
            {
                if (file == null) file = await Task.Run(localFile);   // a GLB image's file: only now that it is decoded
                if (rev != revision || textures.ContainsKey(key) || file == null) return;
                Texture2D t;
                // A TGA / BMP / DDS / .aei (ImageImport): decoded on a worker thread, made into a texture here; PNG / JPG by
                // Unity's decoder below. A file that can't be read is left to Texture, which warns with the reason.
                var other = await Task.Run(() =>
                {
                    var head = new byte[4];
                    using (var f = System.IO.File.OpenRead(file)) f.Read(head, 0, head.Length);
                    if (ImageImport.IsNative(head)) return null;
                    bool ok = ImageImport.DecodePixels(System.IO.File.ReadAllBytes(file), path ?? file, out int w, out int h, out var px, out _);
                    return ok ? new System.Tuple<int, int, Color32[]>(w, h, px) : new System.Tuple<int, int, Color32[]>(0, 0, null);
                });
                if (rev != revision || textures.ContainsKey(key)) return;
                if (other != null)
                {
                    if (other.Item3 == null) return;
                    t = ImageImport.MakeTexture(other.Item1, other.Item2, other.Item3, linear, !encode);
                }
                else
                {
                    var p = UnityEngine.Networking.DownloadedTextureParams.Default;
                    p.mipmapChain = !encode;
                    p.linearColorSpace = linear;
                    p.readable = true;   // the pixels for the encoder / Compress; Apply(.., true) drops them afterwards
                    using var req = UnityEngine.Networking.UnityWebRequestTexture.GetTexture(new System.Uri(file).AbsoluteUri, p);
                    var op = req.SendWebRequest();
                    while (!op.isDone) await Awaitable.NextFrameAsync();
                    if (req.result != UnityEngine.Networking.UnityWebRequest.Result.Success || rev != revision || textures.ContainsKey(key)) return;
                    t = UnityEngine.Networking.DownloadHandlerTexture.GetContent(req);
                    if (t == null) return;
                }
                t.name = name;
                // The encoder converts it for its role on the worker threads too; the main thread only when it can't take it.
                var work = encode ? ModTextureEncoder.Start(t, linear, (ModTextureEncoder.Remap)remap, metal, rough, target) : null;
                if (work == null && remap != Remap.None)
                {
                    t = Convert(t, remap, metal, rough);
                    if (encode) work = ModTextureEncoder.Start(t, linear, target: target);
                }
                if (encode && work == null && loggedFormats.Add(t.format + (t.width % 4 == 0 && t.height % 4 == 0 ? "" : " (size)")))
                    Debug.Log($"Mods: {name}: {t.format} {t.width}x{t.height} is compressed on the main thread (the background encoder takes RGBA32 / ARGB32 / RGB24, sizes in multiples of 4)");
                if (work != null)
                {
                    // Burst jobs compress it with its mipmaps (ModTextureEncoder); the decoded copy goes now.
                    Object.Destroy(t);
                    while (!work.Done) await Awaitable.NextFrameAsync();
                    if (rev != revision || textures.ContainsKey(key)) { work.Dispose(); return; }
                    await UploadTurn();
                    if (rev != revision || textures.ContainsKey(key)) { work.Dispose(); return; }
                    long upload = System.Diagnostics.Stopwatch.GetTimestamp();
                    var c = work.Upload(name);
                    Uploaded(upload);
                    // The cache file written straight from the encoder's buffer, which goes once it is written.
                    if (cacheFile != null) _ = WriteCache(cacheFile, c, linear, work);
                    else work.Dispose();
                    Store(key, c, rev);
                    return;
                }
                if (!readable)
                {
                    // One compression per frame: several finishing together would make one long frame. Phones take the
                    // fast mode (ETC2 in high quality took several times as long, the first start's loading screen).
                    while (compressFrame == Time.frameCount) await Awaitable.NextFrameAsync();
                    compressFrame = Time.frameCount;
                    if (rev != revision || textures.ContainsKey(key)) { Object.Destroy(t); return; }
                    if (t.width % 4 == 0 && t.height % 4 == 0) t.Compress(!Application.isMobilePlatform);
                    if (cacheFile != null) WriteCache(cacheFile, t, linear, t.GetRawTextureData());
                    t.Apply(false, true);
                }
                Store(key, t, rev);
            }
            finally
            {
                slots.Release();
                // A GLB image's file was only the way into the decoder (ModInfo.LocalImage).
                if (content != null && file != null) _ = Task.Run(() => { try { System.IO.File.Delete(file); } catch (System.Exception) { } });
            }
        }

        static int uploadFrame = -1;
        static long uploadTicks;
        const double UploadBudgetMs = 4.0;

        /// <summary>Waits for a frame whose texture uploads so far stay under UploadBudgetMs: many finishing at once (cached
        /// textures read many at a time) would make one long frame.</summary>
        static async Task UploadTurn()
        {
            while (Time.frameCount == uploadFrame && uploadTicks * 1000.0 / System.Diagnostics.Stopwatch.Frequency >= UploadBudgetMs)
                await Awaitable.NextFrameAsync();
            if (Time.frameCount != uploadFrame) { uploadFrame = Time.frameCount; uploadTicks = 0; }
        }

        /// <summary>An upload since 'start' (Stopwatch.GetTimestamp) counted against this frame's budget.</summary>
        static void Uploaded(long start) => uploadTicks += System.Diagnostics.Stopwatch.GetTimestamp() - start;

        /// <summary>Writes the encoder's output on a worker thread, then frees it (back on the main thread).</summary>
        static async Task WriteCache(string cacheFile, Texture2D t, bool linear, ModTextureEncoder.Work work)
        {
            int w = t.width, h = t.height, mips = t.mipmapCount;
            var format = t.format;
            var data = work.Data;
            try { await Task.Run(() => ModTextureCache.Write(cacheFile, w, h, format, mips, linear, data.AsReadOnlySpan())); }
            finally { work.Dispose(); }
        }

        static void WriteCache(string cacheFile, Texture2D t, bool linear, byte[] raw)
        {
            int w = t.width, h = t.height, mips = t.mipmapCount;
            var format = t.format;
            _ = Task.Run(() => ModTextureCache.Write(cacheFile, w, h, format, mips, linear, raw));
        }

        static void Warn(ModInfo mod, string message)
        {
            if (mod.Warnings.Contains(message)) return;
            mod.Warnings.Add(message);
            Debug.LogWarning($"Mods: {mod.Id}: {message}");
        }

        /// <summary>A 1 x 1 texture of one colour (a metallic / smoothness value without a map).</summary>
        static Texture2D Solid(Color c, bool linear = true)
        {
            Color32 k = c;
            if (solids.TryGetValue((k, linear), out var t) && t != null) return t;
            t = new Texture2D(1, 1, TextureFormat.RGBA32, false, linear) { name = "mod solid " + k };
            t.SetPixel(0, 0, c);
            t.Apply(false, true);
            made.Add(t);
            return solids[(k, linear)] = t;
        }

        /// <summary>A copy of a template material, released with the rest when the mods change (EngineFx's).</summary>
        internal static Material CopyOf(Material template, string name) => template != null ? Copy(template, name) : null;

        static Material Copy(Material template, string name)
        {
            Check();
            var m = new Material(template) { name = name };
            made.Add(m);
            return m;
        }

        static Color Rgb(float[] v, Color fallback) => v != null && v.Length >= 3 ? new Color(v[0], v[1], v[2], 1f) : fallback;

        /// <summary>A ships.json material entry (CustomShipMaterial, see CustomShipBuilder in PR #34 for the original).</summary>
        static readonly int GoF3ParamsId = Shader.PropertyToID("_GoF3Params");
        static readonly int GoF3EnvId = Shader.PropertyToID("_GoF3Env"), GoF3EnvMapId = Shader.PropertyToID("_GoF3EnvMap");

        /// <summary>A GoF3 material's own environment cube (a JSON material's or a glTF material's "extras" "environmentCube"):
        /// a 6:1 strip of its faces (right, left, up, down, front, back, as the mods' skies), GoF3's generic cube as GoF3 has
        /// it, monochrome with four blurs in R G B A (R the sharpest), linear. GoF2/Hull's GoF3ShipShader reflects it, its blur
        /// picked by the gloss, as GoF3's ShipShader does; without one the place's environment stands in. Anything but 6:1 is
        /// left out with a warning.</summary>
        internal static void GoF3Environment(Material m, Texture2D strip)
        {
            if (strip != null && Mathf.Abs(strip.width / (float)Mathf.Max(1, strip.height) - 6f) > 0.05f)
            {
                Debug.LogWarning($"Mods: {strip.name} is {strip.width} x {strip.height}: an environment cube is a 6:1 strip of its faces");
                strip = null;
            }
            if (strip == null) { m.SetVector(GoF3EnvId, Vector4.zero); return; }
            m.SetTexture(GoF3EnvMapId, strip);
            m.SetVector(GoF3EnvId, new Vector4(1f, 1f / strip.height, 0f, 0f));
        }

        /// <summary>A material with an emission map or colour draws it: _EMISSION on its copy, whatever the template holds. The
        /// templates' keyword is lost whenever the Editor re-saves them (a shader reimport did, 2026-11-02: every mod room and
        /// glow went dark, the Manticore's hangar a flat grey with GoF3's baked lighting in its emission); ModAssetsGuard
        /// only restores it before a build.</summary>
        static void Glowing(Material m)
        {
            m.EnableKeyword("_EMISSION");
            m.globalIlluminationFlags = MaterialGlobalIlluminationFlags.None;
        }

        static readonly int Es2WearId = Shader.PropertyToID("_Es2Wear"), Es2Wear2Id = Shader.PropertyToID("_Es2Wear2"), Es2GrungeId = Shader.PropertyToID("_Es2Grunge");

        /// <summary>EVERSPACE 2's structure layers (CustomShipMaterial "wear" / "grunge", GoF2/Hull's Es2StructureLayers): on
        /// with either map; off (_Es2Wear2.z 0) otherwise, as the templates have it.</summary>
        static void Es2Layers(ModInfo mod, CustomShipMaterial e, Material m)
        {
            if (string.IsNullOrEmpty(e.wear) && string.IsNullOrEmpty(e.grunge)) return;
            var wear = Texture(mod, e.wear, true);
            m.SetTexture("_Es2WearMap", wear != null ? wear : Solid(new Color(0f, 0f, 0f, 0f)));
            m.SetTexture("_Es2WearNormalMap", Texture(mod, e.wearNormal, true, normal: true));
            var noise = Texture(mod, e.wearNoise, true);
            m.SetTexture("_Es2WearNoiseMap", noise != null ? noise : Solid(Color.white));
            var grunge = Texture(mod, e.grunge, true);
            m.SetTexture("_Es2GrungeMap", grunge != null ? grunge : Solid(Color.white));
            // without a wear map nothing wears (a cap of 0: the threshold below every mask)
            m.SetVector(Es2WearId, new Vector4(Mathf.Max(0.01f, e.wearTiling), Mathf.Max(0.01f, e.wearExponent), wear != null ? Mathf.Clamp01(e.wearCap) : 0f, Mathf.Max(0f, e.wearWidth)));
            m.SetVector(Es2Wear2Id, new Vector4(Mathf.Clamp01(e.wearBrightness), Mathf.Max(0f, e.wearMetal), 1f, e.baseRoughness >= 0f ? Mathf.Clamp01(e.baseRoughness) : -1f));
            m.SetVector(Es2GrungeId, new Vector4(grunge != null ? Mathf.Clamp01(e.grungeAmount) : 0f, Mathf.Max(0.01f, e.grungeTiling), Mathf.Max(0f, e.rimBoost), 0f));
        }

        /// <summary>A "wave" block (GlowWaveSpec, GoF2/Hull's GlowWave): its pattern and mask as data; off (_WaveParams.x 0)
        /// without one, as the templates have it.</summary>
        /// <summary>The "hit" block (GoF2/Hull's HitFlash, World.HullHitFlash drives it): _HitFx (y radius in metres, z duration,
        /// w 1 = its pattern in the wave pattern's alpha, 2 = none), _HitColor; x (the flash's age, 0..1) stays 1 (off).</summary>
        static void HitFlash(CustomShipMaterial e, Material m, bool wave)
        {
            var h = e.hit;
            if (h == null || !m.HasProperty("_HitFx")) return;
            m.SetVector("_HitFx", new Vector4(1f, Mathf.Max(0.01f, h.radius * 0.05f), Mathf.Max(0.001f, h.duration), h.pattern && wave ? 1f : 2f));
            m.SetVector("_HitColor", Rgb(h.color, new Color(750f, 0f, 0f)));
        }

        /// <summary>A "lava" block (ChorusLavaSpec, GoF2/Hull's LavaFlow): its three packed maps as data, repeating; off
        /// (_LavaE.w 0) without one.</summary>
        static void LavaFlow(ModInfo mod, CustomShipMaterial e, Material m)
        {
            var l = e.lava;
            if (l == null || !l.On || !m.HasProperty("_LavaE")) return;
            Texture2D Map(string path)
            {
                var t = Texture(mod, path, true);
                if (t != null) t.wrapMode = TextureWrapMode.Repeat;
                return t;
            }
            var emit = Map(l.emit); var flow = Map(l.flow); var masks = Map(l.masks);
            if (emit == null || flow == null || masks == null) return;
            m.SetTexture("_LavaEmit", emit); m.SetTexture("_LavaFlowMaps", flow); m.SetTexture("_LavaMasks", masks);
            Vector4 V(float[] f) => f != null && f.Length >= 4 ? new Vector4(f[0], f[1], f[2], f[3]) : Vector4.zero;
            m.SetVector("_LavaA", V(l.a)); m.SetVector("_LavaB", V(l.b)); m.SetVector("_LavaC", V(l.c)); m.SetVector("_LavaD", V(l.d));
            m.SetVector("_LavaE", new Vector4(l.magma2Exp, l.tempMin, l.tempMax, Mathf.Max(0f, l.glow)));
            m.SetVector("_LavaF", new Vector4(l.mip, l.mip2, 0f, 0f));
            Glowing(m);
        }

        static bool GlowWave(ModInfo mod, CustomShipMaterial e, Material m)
        {
            var w = e.wave;
            if (w == null || !w.On || !m.HasProperty("_WaveParams")) return false;
            var pattern = Texture(mod, w.pattern, true);
            var mask = Texture(mod, w.mask, true);
            if (pattern == null || mask == null) return false;
            pattern.wrapMode = TextureWrapMode.Repeat;
            m.SetTexture("_WavePattern", pattern);
            m.SetTexture("_WaveMask", mask);
            m.SetVector("_WaveParams", new Vector4(Mathf.Max(0f, w.multiplier), Mathf.Max(1e-3f, w.tiling), w.speed, Mathf.Clamp(w.channel, 0, 2)));
            m.SetVector("_WaveMin", Rgb(w.min, Color.black));
            m.SetVector("_WaveMax", Rgb(w.max, Color.white));
            return true;
        }

        static readonly int ChorusParamsId = Shader.PropertyToID("_ChorusParams"), ChorusAccentId = Shader.PropertyToID("_ChorusAccent"),
            ChorusAccent2Id = Shader.PropertyToID("_ChorusAccent2"), ChorusEmitRId = Shader.PropertyToID("_ChorusEmitR"),
            ChorusEmitGId = Shader.PropertyToID("_ChorusEmitG"), ChorusEmitBId = Shader.PropertyToID("_ChorusEmitB"),
            ChorusKickerId = Shader.PropertyToID("_ChorusKicker"), ChorusKicker2Id = Shader.PropertyToID("_ChorusKicker2"),
            ChorusDirtId = Shader.PropertyToID("_ChorusDirt"), ChorusDirt2Id = Shader.PropertyToID("_ChorusDirt2"),
            ChorusDirt3Id = Shader.PropertyToID("_ChorusDirt3");

        static Vector4 Rgba(float[] c, float a) => c != null && c.Length >= 3 ? new Vector4(c[0], c[1], c[2], a) : new Vector4(0f, 0f, 0f, a);

        /// <summary>The "tissue" kind (Chorus's M_JTU_Faceless; GoF2/Hull's ChorusTissue): its normal in URP's bump slot, the AO in
        /// the occlusion slot, the thickness in the metallic slot, the outer variation in the detail colour slot, the inner
        /// variation and the ice in the wear slots, the dirt in the palette slot, the breath row in the dirt roughness slot; its
        /// numbers in the _Chorus* vectors, _ChorusParams.x 5.</summary>
        static void ChorusTissue(ModInfo mod, ChorusLayersSpec c, Material m)
        {
            Texture2D Map(string path, Color none)
            {
                var t = Texture(mod, path, true);
                if (t == null) return Solid(none, true);
                t.wrapMode = TextureWrapMode.Repeat;
                return t;
            }
            var black = Solid(Color.black, false);
            m.SetTexture("_BaseMap", black); m.SetTexture("_MainTex", black);
            m.SetColor("_BaseColor", Color.white);
            var nrm = Texture(mod, c.normal, true, normal: true);
            m.SetTexture("_BumpMap", nrm);
            m.EnableKeyword("_NORMALMAP");   // (the tangent frame: the parallax)
            m.SetTexture("_OcclusionMap", Map(c.ao, Color.white));
            m.SetTexture("_MetallicGlossMap", Map(c.thickness, new Color(0.5f, 0f, 0f, 1f)));
            m.SetTexture("_DetailAlbedoMap", Map(c.outerVariation, Color.black));
            m.SetTexture("_Es2WearMap", Map(c.innerVariation, Color.black));
            m.SetTexture("_Es2WearNoiseMap", Map(c.ice, Color.black));
            m.SetTexture("_ChorusPalette", Map(c.dirt, Color.black));
            var breath = Texture(mod, c.breath, true);
            if (breath != null) breath.wrapMode = TextureWrapMode.Clamp;
            m.SetTexture("_ChorusDirtRough", breath != null ? breath : Solid(Color.black, true));
            m.SetTexture("_EmissionMap", null);
            m.SetColor("_EmissionColor", Color.black);   // (the layer makes the emission itself)
            m.SetVector(ChorusParamsId, new Vector4(5f, 1f, 1f, Mathf.Max(0f, c.emissionScale)));
            bool core = c.coreColor != null && c.coreColor.Length >= 3 && c.coreContrast > 0f;
            m.SetVector(ChorusAccentId, core ? Rgba(c.coreColor, c.coreContrast) : Vector4.zero);
            m.SetVector(ChorusEmitRId, Rgba(c.outerGlow ?? new[] { 0.17f, 0f, 0f }, 0f));
            m.SetVector(ChorusEmitGId, Rgba(c.innerGlow ?? new[] { 3f, 0f, 0.08f }, 0f));
            m.SetVector(ChorusEmitBId, new Vector4(Mathf.Max(1e-3f, c.noiseTiling), c.depth, Mathf.Max(0f, c.strength), Mathf.Clamp01(c.roughnessIntensity)));
            m.SetVector(ChorusKickerId, new Vector4(Mathf.Max(0f, c.breathInterval), Mathf.Max(1f, c.pulsePeriod), 0f, 0f));
            m.SetVector(ChorusKicker2Id, new Vector4(0f, 1f, 1f, 0f));
            var so = c.sphereOffset != null && c.sphereOffset.Length >= 3 ? c.sphereOffset : new float[3];
            m.SetVector(ChorusDirtId, core ? new Vector4(so[0], so[1], so[2], Mathf.Max(1e-3f, c.sphereRadius)) : Vector4.zero);
            m.SetVector(ChorusDirt2Id, Vector4.one);
            m.SetVector(ChorusDirt3Id, Vector4.zero);
        }

        /// <summary>The "crystal" kind (Chorus's M_JTU_Blend; GoF2/Hull's ChorusCrystal): its maps in the slots the HLSL reads
        /// them from (A's in URP's base / bump / metallic, B's in the detail and occlusion slots, the masks in the emission and
        /// palette slots, the veins and variation in the wear slots, the breath row in the dirt roughness slot), its numbers in
        /// the _Chorus* vectors, _ChorusParams.x 4.</summary>
        static void ChorusCrystal(ModInfo mod, ChorusLayersSpec c, Material m)
        {
            Texture2D Map(string path, bool linear, Color none, bool normal = false)
            {
                var t = Texture(mod, path, linear, normal: normal);
                if (t == null) return normal ? null : Solid(none, linear);
                t.wrapMode = TextureWrapMode.Repeat;
                return t;
            }
            var a = Map(c.diffuseA, false, Color.grey);
            m.SetTexture("_BaseMap", a); m.SetTexture("_MainTex", a);
            m.SetTextureScale("_BaseMap", Vector2.one); m.SetTextureScale("_MainTex", Vector2.one);
            m.SetColor("_BaseColor", Color.white);
            m.SetTexture("_BumpMap", Map(c.normalA, true, Color.grey, true));
            m.EnableKeyword("_NORMALMAP");   // (the tangent frame the surfaces are mapped along)
            m.SetTexture("_MetallicGlossMap", Map(c.mroA, true, new Color(0f, 0.5f, 1f, 1f)));
            m.SetTexture("_EmissionMap", Map(c.maskA, true, Color.black));
            m.SetColor("_EmissionColor", Color.black);   // (the layer makes the emission itself)
            m.SetTexture("_DetailAlbedoMap", Map(c.diffuseB, false, Color.grey));
            m.SetTexture("_DetailNormalMap", Map(c.normalB, true, Color.grey, true));
            m.SetTexture("_OcclusionMap", Map(c.mroB, true, new Color(0f, 0.5f, 1f, 1f)));
            m.SetTexture("_ChorusPalette", Map(c.maskB, true, Color.black));
            m.SetTexture("_Es2WearMap", Map(c.heightmap, true, Color.grey));
            m.SetTexture("_Es2WearNoiseMap", Map(c.variation, true, Color.grey));
            var breath = Texture(mod, c.breath, true);
            if (breath != null) breath.wrapMode = TextureWrapMode.Clamp;
            m.SetTexture("_ChorusDirtRough", breath != null ? breath : Solid(new Color(1f, 0f, 0f, 1f)));
            m.SetVector(ChorusParamsId, new Vector4(4f, Mathf.Max(1f, c.scaleA), Mathf.Max(1f, c.scaleB), Mathf.Max(0f, c.emissionScale)));
            m.SetVector(ChorusAccentId, Rgba(c.macroColor ?? new[] { 0f, 0f, 0f }, c.macroColor != null ? Mathf.Clamp01(c.macroAmount) : 0f));
            m.SetVector(ChorusAccent2Id, new Vector4(c.macroTiling, Mathf.Clamp01(c.macroRoughness), Mathf.Clamp01(c.macroRoughnessAmount), 0.5f));
            m.SetVector(ChorusEmitRId, Rgba(c.innerGlow ?? new[] { 1f, 0.05f, 0.05f }, Mathf.Max(0f, c.glowA)));
            m.SetVector(ChorusEmitGId, new Vector4(Mathf.Max(0f, c.glowPulseB), Mathf.Max(0f, c.glowB), c.innerRefraction, c.innerHeightRatio));
            m.SetVector(ChorusEmitBId, new Vector4(Mathf.Max(1f, c.innerScale), Mathf.Max(1f, c.noiseScale), Mathf.Max(0f, c.noiseContrast), c.noiseOffset));
            m.SetVector(ChorusKickerId, new Vector4(Mathf.Max(0f, c.breathInterval), Mathf.Max(1f, c.pulsePeriod), c.breathAmount, Mathf.Max(1e-4f, c.transitionSoftness)));
            m.SetVector(ChorusKicker2Id, new Vector4(c.normalIntensityA, c.normalIntensityB, 1f, 0f));
            m.SetVector(ChorusDirtId, new Vector4(c.roughnessBiasA, c.roughnessBiasB, 0f, 0f));
            m.SetVector(ChorusDirt2Id, Vector4.one);
            m.SetVector(ChorusDirt3Id, Vector4.zero);
        }

        /// <summary>Chorus's trim sheets and platings (CustomShipMaterial "chorus", GoF2/Hull's ChorusLayers): its maps in URP's
        /// slots, which the layer reads in their place (_BaseMap the tiling diffuse / plating albedo, _MetallicGlossMap its RMO /
        /// ORM, _DetailNormalMap the tiling normal, _BumpMap the trim normal (the plating's normal), _OcclusionMap Trim AAO,
        /// _EmissionMap Trim Emissive, _DetailAlbedoMap / _DetailMask the dirt and its mask), and the palette and dirt roughness
        /// in their own. The template's keywords stay (no detail or occlusion keyword: Lit doesn't apply them itself). Off
        /// (_ChorusParams.x 0) without a kind or a palette, as the templates have it.</summary>
        static void ChorusLayers(ModInfo mod, CustomShipMaterial e, Material m)
        {
            var c = e.chorus;
            if (c == null || !c.On) return;
            if (c.Shell) { ChorusShell(mod, c, m); return; }
            if (c.Crystal) { ChorusCrystal(mod, c, m); return; }
            if (c.Tissue) { ChorusTissue(mod, c, m); return; }
            var palette = Texture(mod, c.palette, true);
            if (palette == null) return;
            bool plating = c.Plating;
            m.SetTexture("_ChorusPalette", palette);
            var diffuse = Texture(mod, c.tilingDiffuse, false);
            if (diffuse == null) diffuse = Solid(Color.white, false);
            m.SetTexture("_BaseMap", diffuse);
            m.SetTexture("_MainTex", diffuse);
            m.SetTextureScale("_BaseMap", Vector2.one);   // (the layer scales its own UVs)
            m.SetTextureScale("_MainTex", Vector2.one);
            m.SetColor("_BaseColor", Color.white);
            var rmo = Texture(mod, c.tilingRmo, true);
            // without one: mid roughness, no metal, no occlusion (trim RMO: R G B; plating ORM: R G B)
            m.SetTexture("_MetallicGlossMap", rmo != null ? rmo : Solid(plating ? new Color(1f, 0.5f, 0f, 1f) : new Color(0.5f, 0f, 1f, 1f)));
            m.SetTexture(plating ? "_BumpMap" : "_DetailNormalMap", Texture(mod, c.tilingNormal, true, normal: true));
            if (!plating)
            {
                m.SetTexture("_BumpMap", Texture(mod, c.trimNormal, true, normal: true));
                var ao = Texture(mod, c.trimAo, true);
                m.SetTexture("_OcclusionMap", ao != null ? ao : Solid(new Color(0f, 1f, 1f, 1f)));
            }
            var emissive = plating ? null : Texture(mod, c.trimEmissive, false);
            m.SetTexture("_EmissionMap", emissive);
            m.SetColor("_EmissionColor", Color.black);   // (the layer makes the emission itself)
            float kind = plating ? 2f : 1f;
            m.SetVector(ChorusParamsId, new Vector4(kind, Mathf.Max(1e-4f, c.tiling), Mathf.Clamp01(c.aoIntensity), Mathf.Max(0f, c.emissionScale)));
            bool accent = !plating && c.accentColor != null && c.accentColor.Length >= 3;
            m.SetVector(ChorusAccentId, Rgba(c.accentColor, accent ? 1f : 0f));
            m.SetVector(ChorusAccent2Id, new Vector4(c.accentMetal, c.accentRoughness, c.accentNormal, 0f));
            m.SetVector(ChorusEmitRId, Rgba(c.emissiveR, emissive != null ? Mathf.Max(0f, c.emissiveAmount) : 0f));
            m.SetVector(ChorusEmitGId, Rgba(c.emissiveG, c.emissiveUvShift ? 1f : 0f));
            m.SetVector(ChorusEmitBId, Rgba(c.emissiveB, c.distanceBoost));
            m.SetVector(ChorusKickerId, Rgba(c.kickerColor, c.kickerColor != null ? Mathf.Max(0f, c.kickerExp) : 0f));
            m.SetVector(ChorusKicker2Id, new Vector4(c.kickerFadeOffset, Mathf.Max(1e-3f, c.kickerFadeLength), Mathf.Max(0f, c.nearEmission), 0f));
            var dirt = Texture(mod, c.dirt, false);
            m.SetVector(ChorusDirtId, Rgba(c.dirtTint ?? new[] { 1f, 1f, 1f }, dirt != null ? 1f : 0f));
            m.SetVector(ChorusDirt2Id, new Vector4(c.dirtUvScale, c.dirtMaskUvScale, c.dirtAo, c.dirtContrast));
            m.SetVector(ChorusDirt3Id, new Vector4(c.dirtAlpha ? 1f : 0f, c.dirtByAlpha ? 1f : 0f, c.dirtOverEmissive ? 1f : 0f, 0f));
            if (dirt != null)
            {
                m.SetTexture("_DetailAlbedoMap", dirt);
                var mask = Texture(mod, c.dirtMask, true);
                m.SetTexture("_DetailMask", mask != null ? mask : Solid(Color.white));
                var dr = Texture(mod, c.dirtRoughness, true);
                m.SetTexture("_ChorusDirtRough", dr != null ? dr : Solid(new Color(0.5f, 0f, 1f, 1f)));
            }
        }

        /// <summary>Chorus's M_Guilt_Shell (a "chorus" block of kind "shell"; GoF2/Hull's ChorusShell): the mask in _BaseMap
        /// (the shadow pass reads its alpha), the flowing noise in _OcclusionMap, the flow vectors in _DetailNormalMap (read as
        /// plain data), the flow's phase in _DetailMask; its values in _ChorusParams (x 3, y tiling, z randomness, w the
        /// emission's scale), _ChorusAccent (rgb emissiveColor, a emissiveIntensity), _ChorusAccent2 (x emissiveExponent, y
        /// centerRadius, z flowSpeed, w flowStrength), _ChorusEmitR (x armsFade, y coverage, z erode, w masterAlpha),
        /// _ChorusEmitG (x push, y pushNoise, z baseColor, w metallic), _ChorusEmitB (x roughness).</summary>
        static void ChorusShell(ModInfo mod, ChorusLayersSpec c, Material m)
        {
            var mask = Texture(mod, c.mask, true);
            if (mask == null) return;
            m.SetTexture("_BaseMap", mask); m.SetTexture("_MainTex", mask);
            m.SetTextureScale("_BaseMap", Vector2.one); m.SetTextureScale("_MainTex", Vector2.one);
            m.SetColor("_BaseColor", Color.white);
            var noise = Texture(mod, c.noise, true);
            m.SetTexture("_OcclusionMap", noise != null ? noise : Solid(new Color(0.5f, 0.5f, 0.5f, 1f)));
            var flow = Texture(mod, c.flow, true);
            m.SetTexture("_DetailNormalMap", flow != null ? flow : Solid(new Color(0.5f, 0.5f, 1f, 1f)));
            var phase = Texture(mod, c.phase, true);
            m.SetTexture("_DetailMask", phase != null ? phase : Solid(new Color(0.5f, 0.5f, 0.5f, 1f)));
            var glow = Texture(mod, c.glow, true);
            m.SetTexture("_EmissionMap", glow != null ? glow : mask);   // (none: the mask, as before)
            // its normal (the noise's red and green) needs the tangent frame; the alpha test carries the UVs to the shadow and
            // depth passes for its dissolve (a cutoff of 0: URP's own test never clips)
            m.EnableKeyword("_NORMALMAP");
            m.EnableKeyword("_ALPHATEST_ON");
            m.SetFloat("_AlphaClip", 1f);
            m.SetFloat("_Cutoff", 0f);
            m.SetColor("_EmissionColor", Color.black);
            m.SetVector(ChorusParamsId, new Vector4(3f, Mathf.Max(1e-4f, c.tiling), Mathf.Max(1e-4f, c.randomness), Mathf.Max(0f, c.emissionScale)));
            m.SetVector(ChorusAccentId, Rgba(c.emissiveColor, Mathf.Max(0f, c.emissiveIntensity)));
            m.SetVector(ChorusAccent2Id, new Vector4(c.emissiveExponent, Mathf.Max(1e-4f, c.centerRadius), c.flowSpeed, c.flowStrength));
            m.SetVector(ChorusEmitRId, new Vector4(c.armsFade, c.coverage, Mathf.Max(1e-4f, c.erode), c.masterAlpha));
            m.SetVector(ChorusEmitGId, new Vector4(c.push, c.pushNoise, Mathf.Clamp01(c.baseColor), Mathf.Clamp01(c.metal)));
            m.SetVector(ChorusEmitBId, new Vector4(Mathf.Clamp01(c.roughness), 0f, 0f, 0f));
            m.SetVector(ChorusKickerId, Vector4.zero);
            m.SetVector(ChorusDirtId, Vector4.zero);
        }

        static Shader portalShader;

        /// <summary>A "portal" block (Chorus's FTL portal, its glow or Guilt's core) on GoF2/ChorusPortal: its maps as data (linear), the curve
        /// row clamped; World.PortalRefraction gives it the scene behind (URP's opaque texture).</summary>
        static Material Portal(ModInfo mod, ChorusPortalSpec p, string name)
        {
            if (portalShader == null) portalShader = Resources.Load<Shader>("GoF2Fx/ChorusPortal");
            if (portalShader == null) return null;
            var m = new Material(portalShader) { name = name };
            void Map(string prop, string path, TextureWrapMode wrap)
            {
                var t = Texture(mod, path, true);
                if (t == null) return;
                t.wrapMode = wrap;
                m.SetTexture(prop, t);
            }
            Map("_Noise", p.noise, TextureWrapMode.Repeat);
            Map("_Height", p.height, TextureWrapMode.Clamp);
            Map("_Curve", p.curve, TextureWrapMode.Clamp);
            Map("_SphereNormal", p.sphereNormal, TextureWrapMode.Clamp);
            Map("_RingNormal", p.ringNormal, TextureWrapMode.Clamp);
            Map("_Gradient", p.gradient, TextureWrapMode.Repeat);
            m.SetFloat("_Kind", p.Core ? 2f : p.Glow ? 1f : 0f);
            m.SetFloat("_SrcBlend", (float)(p.Core ? UnityEngine.Rendering.BlendMode.SrcAlpha : UnityEngine.Rendering.BlendMode.One));
            m.SetFloat("_DstBlend", (float)(p.Core ? UnityEngine.Rendering.BlendMode.OneMinusSrcAlpha : UnityEngine.Rendering.BlendMode.One));
            m.SetVector("_CoreUV", new Vector4(p.tilingU, p.tilingV, p.speedU, p.panningV));
            m.SetVector("_CoreShape", new Vector4(p.pinch, p.corePower, p.coreMult, p.threadsPower));
            m.SetVector("_CoreAlpha", new Vector4(p.threadsMult, p.masterAlpha, Mathf.Max(0f, p.depthFade) * 0.05f, p.fade));
            m.SetVector("_Polar", new Vector4(p.tilingV, p.panningV, p.structurePower, p.structureMult));
            m.SetVector("_Bump", new Vector4(p.heightRatio, p.fade, p.ior, p.refraction));
            m.SetVector("_Pulse", new Vector4(p.pulseSpeed, p.pulseB, 0f, 0f));
            var c = p.color != null && p.color.Length >= 3 ? p.color : new[] { 1f, 1f, 1f };
            m.SetVector("_GlowColor", new Vector4(c[0], c[1], c[2], p.lerpColor));
            return m;
        }

        static Shader shieldShader;

        /// <summary>A "shield" block (Chorus's M_Shield) on GoF2/ChorusShield: its maps as data (linear), repeating; its lengths
        /// as given (model units), the fades game units to metres.</summary>
        static Material Shield(ModInfo mod, ChorusShieldSpec p, string name)
        {
            if (shieldShader == null) shieldShader = Resources.Load<Shader>("GoF2Fx/ChorusShield");
            if (shieldShader == null) return null;
            var m = new Material(shieldShader) { name = name };
            void Map(string prop, string path)
            {
                var t = Texture(mod, path, true);
                if (t == null) return;
                t.wrapMode = TextureWrapMode.Repeat;
                m.SetTexture(prop, t);
            }
            Map("_Mask", p.mask); Map("_Noise", p.noise);
            Vector4 V(float[] f, Vector4 d) => f == null ? d : new Vector4(f.Length > 0 ? f[0] : d.x, f.Length > 1 ? f[1] : d.y, f.Length > 2 ? f[2] : d.z, f.Length > 3 ? f[3] : d.w);
            const float M = 0.05f;
            m.SetVector("_ShieldPan", V(p.pan, new Vector4(-0.42435f, 0f, 0f, -0.37348f)));
            m.SetVector("_ShieldNoise", new Vector4(p.noiseScale, p.noiseTimeScalar, p.noisePanSpeed, p.timeExponent));
            m.SetVector("_ShieldEdge", new Vector4(p.edgeModulateAmount, p.edgeModulateIntensity, Mathf.Max(1e-5f, p.edgeModulateWidth), p.fresnelExponent));
            m.SetVector("_ShieldMod", new Vector4(p.modulateColorAmount, p.timeModulationAmount, p.opacityNoise, 0f));
            var r = V(p.radius, new Vector4(0f, 4.5f, 0f, 0f));
            m.SetVector("_ShieldRadius", new Vector4(r.x, r.y, p.radiusExponent, p.radiusIntensity));
            m.SetVector("_ShieldRing", V(p.ring, new Vector4(0f, 3.5f, 0.25f, 0f)));
            m.SetVector("_ShieldRing2", new Vector4(p.ringExponent, p.ringIntensity, p.opacityStart, p.opacityEnd));
            var cf = V(p.cameraFade, new Vector4(720f, 288f, 0f, 0f));
            m.SetVector("_ShieldFade", new Vector4(p.opacityFacing, p.opacityBackface, Mathf.Max(0f, cf.x) * M, Mathf.Max(0f, p.exposure)));
            m.SetVector("_ShieldDepth", new Vector4(Mathf.Max(1e-3f, cf.y * M), Mathf.Max(0f, p.depthFade) * M, 0f, 0f));
            return m;
        }

        static Shader riftShader;

        /// <summary>A "rift" block (Chorus's rift vortex) on GoF2/ChorusRift: its maps as data (linear), repeating.</summary>
        static Material Rift(ModInfo mod, ChorusRiftSpec r, string name)
        {
            if (riftShader == null) riftShader = Resources.Load<Shader>("GoF2Fx/ChorusRift");
            if (riftShader == null) return null;
            var m = new Material(riftShader) { name = name };
            void Map(string prop, string path)
            {
                var t = Texture(mod, path, true);
                if (t == null) return;
                t.wrapMode = TextureWrapMode.Repeat;
                m.SetTexture(prop, t);
            }
            Map("_Tiled", r.tiled); Map("_Strip", r.strip); Map("_Lightning", r.lightning);
            Map("_CloudMask", r.cloudMask); Map("_Cloud", r.cloud); Map("_Rock", r.rock);
            Vector4 V(float[] f, Vector4 d) => f == null ? d : new Vector4(f.Length > 0 ? f[0] : d.x, f.Length > 1 ? f[1] : d.y, f.Length > 2 ? f[2] : d.z, f.Length > 3 ? f[3] : d.w);
            m.SetVector("_MainUV", V(r.mainUV, new Vector4(1.5f, 1f, 0.01f, 0.03f)));
            m.SetVector("_DetailUV", V(r.detailUV, new Vector4(1f, 1.5f, 0f, 0.02f)));
            m.SetVector("_ParUV", V(r.cloudsUV, new Vector4(2f, 1f, 0.018f, 0.02f)));
            m.SetVector("_RockUV", V(r.rockUV, new Vector4(3f, 1f, 0.009f, 0.018f)));
            m.SetVector("_Shape", new Vector4(r.mainPow, r.detailPow, r.detailMul, r.redCenter));
            var off = V(r.detailOffset, new Vector4(0.4f, 0.4f, 0f, 0f));
            m.SetVector("_Detail2", new Vector4(off.x, off.y, r.cloudsPow, r.heightRatio));
            var pan = V(r.heightPan, new Vector4(0.018f, 0.02f, 0f, 0f));
            m.SetVector("_HeightPan", new Vector4(pan.x, pan.y, r.cloudPow, r.cloudMul));
            m.SetVector("_Bolts", new Vector4(r.boltSpeed, r.pulseRate1, r.pulseRate2, r.sequenceTime));
            m.SetVector("_Bolts2", new Vector4(Mathf.Max(1f, r.frames), r.lightningMul, r.lightningCloud, r.gradation));
            m.SetVector("_Blend", new Vector4(r.blendExp, r.blendMult, r.corePow, r.coreTint));
            m.SetVector("_Inner", V(r.inner, new Vector4(0.1f, 0f, 0.003f, 0f)));
            m.SetVector("_Outer", V(r.outer, new Vector4(0.1f, 0f, 0.0333f, 0f)));
            m.SetVector("_Core", V(r.core, Vector4.zero));
            m.SetVector("_Brightness", V(r.brightness, new Vector4(200f, 0.8f, 5f, 0f)));
            m.SetVector("_Background", V(r.background, new Vector4(0.01f, 0.013f, 0.02f, 0f)));
            m.SetVector("_Clouds", V(r.clouds, new Vector4(0.0064f, 0.0061f, 0.02f, 0f)));
            m.SetFloat("_Exposure", Mathf.Max(0f, r.exposure));
            // M_PortalRift_BOTTOM ("bottom"): its colours, alpha-blended, its fades (game units -> metres)
            bool bottom = r.Bottom;
            m.SetFloat("_Mode", bottom ? 1f : 0f);
            m.SetFloat("_SrcBlend", (float)(bottom ? UnityEngine.Rendering.BlendMode.SrcAlpha : UnityEngine.Rendering.BlendMode.One));
            m.SetFloat("_DstBlend", (float)(bottom ? UnityEngine.Rendering.BlendMode.OneMinusSrcAlpha : UnityEngine.Rendering.BlendMode.One));
            m.SetVector("_Emissive", V(r.emissive, new Vector4(0.05f, 0.0075f, 0.0088f, 0f)));
            m.SetVector("_BaseCol", V(r.baseColor, new Vector4(0.05f, 0.0097f, 0.0092f, 0f)));
            m.SetVector("_Fade", new Vector4(Mathf.Max(0f, r.depthFade) * 0.05f, Mathf.Max(0f, r.fadeOffset) * 0.05f, Mathf.Max(0f, r.fadeLength) * 0.05f, 0f));
            // the vertex shader's offset along the normal
            Map("_WpoMap", r.wpoMap);
            var w = V(r.wpo, new Vector4(0f, 1f, 2f, 0f));
            m.SetVector("_Wpo", new Vector4(string.IsNullOrEmpty(r.wpoMap) ? 0f : w.x, 0f, 0f, 0f));
            m.SetVector("_WpoUV", r.wpo != null && r.wpo.Length >= 5 ? new Vector4(r.wpo[1], r.wpo[2], r.wpo[3], r.wpo[4]) : new Vector4(1f, 2f, 0.01f, 0.04f));
            return m;
        }

        public static Material FromSpec(ModInfo mod, CustomShipMaterial e, string name)
        {
            var a = ModAssets.Get();
            if (a == null) return null;
            if (e.portal != null && e.portal.On) return Portal(mod, e.portal, name);
            if (e.rift != null && e.rift.On) return Rift(mod, e.rift, name);
            if (e.shield != null && e.shield.On) return Shield(mod, e.shield, name);
            if (e.flame) return Flame(mod, e, name, a);
            if (e.hologram) return Hologram(mod, e, name, a);
            if (e.shimmer) return Shimmer(mod, e, name, a);
            if (e.additive) return Glow(mod, e, name, a);
            bool clip = e.alphaClip > 0f, glass = e.opacity > 0f && e.opacity < 1f;
            var dAlb = Texture(mod, e.detailAlbedo, true);
            var dNrm = Texture(mod, e.detailNormal, true, normal: true);
            // Battle damage charring takes the detail layer when the entry has no detail maps of its own.
            var cAlb = dAlb == null && dNrm == null ? Texture(mod, e.damageDetail, false) : null;
            var cNrm = cAlb != null ? Texture(mod, e.damageNormal, true, normal: true) : null;
            bool specular = !string.IsNullOrEmpty(e.specularGlossiness) || (e.specularColor != null && e.specularColor.Length >= 3);
            var m = Copy(a.Lit(clip && !glass, glass, dAlb != null || dNrm != null || cAlb != null, specular), name);
            m.SetTexture("_DetailAlbedoMap", null);
            m.SetTexture("_DetailNormalMap", null);
            var diffuse = Texture(mod, e.diffuse, false);
            m.SetTexture("_BaseMap", diffuse);
            m.SetTexture("_MainTex", diffuse);
            if (e.tiling > 0f && !Mathf.Approximately(e.tiling, 1f))
            {
                // (URP Lit's base, normal, metallic and emission maps share _BaseMap's scale)
                m.SetTextureScale("_BaseMap", Vector2.one * e.tiling);
                m.SetTextureScale("_MainTex", Vector2.one * e.tiling);
            }
            var baseColor = Rgb(e.color, Color.white);
            if (glass) baseColor.a = e.opacity;
            m.SetColor("_BaseColor", baseColor);
            var nrm = Texture(mod, e.normal, true, normal: true);
            m.SetTexture("_BumpMap", nrm);
            m.SetFloat("_BumpScale", e.normalScale > 0f ? e.normalScale : 1f);
            if (specular)
            {
                // URP's specular setup: the map's RGB the specular colour (sRGB), its A the smoothness (x _Smoothness)
                var sg = Texture(mod, e.specularGlossiness, false);
                m.SetTexture("_SpecGlossMap", sg != null ? sg : Solid(Rgb(e.specularColor, new Color(0.2f, 0.2f, 0.2f)), false));
                m.SetColor("_SpecColor", Color.white);
                m.SetVector(GoF3ParamsId, ModGlbImages.GoF3Params(e.environment, e.rim, e.highlightMultiplier, e.multiplierInMap));
                GoF3Environment(m, Texture(mod, e.environmentCube, true));
            }
            else
            {
                var ms = Texture(mod, e.metallicSmoothness, true);
                m.SetTexture("_MetallicGlossMap", ms != null ? ms : Solid(new Color(e.metallic >= 0f ? e.metallic : 0f, 0f, 0f, 1f)));
            }
            m.SetFloat("_Smoothness", e.smoothness);
            Es2Layers(mod, e, m);
            ChorusLayers(mod, e, m);
            HitFlash(e, m, GlowWave(mod, e, m));
            LavaFlow(mod, e, m);
            var em = Texture(mod, e.emission, false);
            bool emColor = e.emissionColor != null && e.emissionColor.Length >= 3;
            if (em != null || emColor)
            {
                m.SetTexture("_EmissionMap", em);
                var ec = Rgb(e.emissionColor, Color.white) * e.emissionIntensity;
                ec.a = 1f;
                m.SetColor("_EmissionColor", ec);
                Glowing(m);
            }
            else m.SetColor("_EmissionColor", Color.black);
            if (em == null && !emColor && !string.IsNullOrEmpty(e.damageEmission))
            {
                // Battle damage embers: dark until DamageEmbers lights them with the hull damage.
                var dmg = Texture(mod, e.damageEmission, false);
                if (dmg != null) { m.SetTexture("_EmissionMap", dmg); m.EnableKeyword("_EMISSION"); m.globalIlluminationFlags = MaterialGlobalIlluminationFlags.None; m.SetOverrideTag(DamageEmbersTag, "1"); }
            }
            if (clip && !glass) m.SetFloat("_Cutoff", e.alphaClip);
            m.SetFloat("_Cull", e.doubleSided ? (float)CullMode.Off : (float)CullMode.Back);
            if (dAlb != null || dNrm != null)
            {
                m.SetTexture("_DetailAlbedoMap", dAlb);
                m.SetTexture("_DetailNormalMap", dNrm);
                m.SetFloat("_DetailAlbedoMapScale", 1f);
                m.SetFloat("_DetailNormalMapScale", e.detailNormalScale > 0f ? e.detailNormalScale : 1f);
                var tiling = Vector2.one * (e.detailTiling > 0f ? e.detailTiling : 1f);
                m.SetTextureScale("_DetailAlbedoMap", tiling);
                m.SetTextureScale("_DetailNormalMap", tiling);
            }
            else if (cAlb != null)
            {
                // Charring: off until DamageEmbers raises the strength with the hull damage.
                m.SetTexture("_DetailAlbedoMap", cAlb);
                m.SetTexture("_DetailMask", cAlb);
                m.SetTexture("_DetailNormalMap", cNrm);
                m.SetFloat("_DetailAlbedoMapScale", 0f);
                m.SetFloat("_DetailNormalMapScale", 0f);
                m.DisableKeyword("_DETAIL_MULX2");
                m.EnableKeyword("_DETAIL_SCALED");   // (MULX2 ignores the scale)
            }
            return m;
        }

        /// <summary>EVERSPACE 2's flame emits 1000 x its colour, alpha-blended by its opacity, which its exposure brings down:
        /// the remake adds colour x opacity x this (matched by eye against EVERSPACE 2 screenshots of the Wraith: a short
        /// white-hot core ringed in orange, a little longer than its nozzle is wide).</summary>
        public static float Es2FlameGain = 300f;

        /// <summary>A "flame" entry: a copy of GoF2/ModEngineFlame (EngineFlame drives it per ship) in the entry's colour
        /// (emissionColor x emissionIntensity, the kit colour slots' emission target too), boost colour, length and noise.</summary>
        static Material Flame(ModInfo mod, CustomShipMaterial e, string name, ModAssets a)
        {
            if (a.engineFlame == null) { Debug.LogWarning("Mods: no engine flame template (GoF2 > Build > Mod Assets)"); return null; }
            var m = Copy(a.engineFlame, name);
            float k = e.emissionIntensity > 0f ? e.emissionIntensity : 1f;
            var c = Rgb(e.emissionColor, new Color(1f, 0.4f, 0.1f)) * k; c.a = 1f;
            m.SetColor("_EmissionColor", c);
            var b = Rgb(e.boostColor, new Color(0.2f, 0.6f, 1f)) * k; b.a = 1f;
            m.SetColor("_BoostColor", b);
            m.SetFloat("_Length", e.flameLength > 0f ? e.flameLength : 5f);
            m.SetFloat("_IdleLength", Mathf.Clamp01(e.flameIdle));
            // EVERSPACE 2's own exhaust formula (GoF2/ModEngineFlame's _Es2: its M_Vehicle_EngineExhaust, decompiled)
            m.SetFloat("_Es2", 1f);
            m.SetFloat("_Brightness", Es2FlameGain);
            var noise = Texture(mod, e.flameNoise, true);
            if (noise != null) { noise.wrapMode = TextureWrapMode.Repeat; m.SetTexture("_NoiseMap", noise); }
            else m.SetFloat("_NoiseStrength", 0f);
            return m;
        }

        /// <summary>A "hologram" entry: a copy of GoF2/ModHologram in the entry's colour (emissionColor x emissionIntensity, the
        /// kit colour slots' emission target too) with its pattern (emission).</summary>
        static Material Hologram(ModInfo mod, CustomShipMaterial e, string name, ModAssets a)
        {
            if (a.hologram == null) { Debug.LogWarning("Mods: no hologram template (GoF2 > Build > Mod Assets)"); return null; }
            var m = Copy(a.hologram, name);
            float k = e.emissionIntensity > 0f ? e.emissionIntensity : 1f;
            var c = Rgb(e.emissionColor, new Color(0.05f, 0.525f, 1f)) * k; c.a = 1f;
            m.SetColor("_EmissionColor", c);
            var pattern = Texture(mod, e.emission, false);
            if (pattern != null) m.SetTexture("_EmissionMap", pattern);
            return m;
        }

        /// <summary>An "additive" entry: a copy of GoF2/ModGlow, its texture (emission, else diffuse) x emissionColor (else
        /// color, else white) x emissionIntensity x the vertex colours, scrolling by "scroll".</summary>
        static Material Glow(ModInfo mod, CustomShipMaterial e, string name, ModAssets a)
        {
            if (a.glow == null) { Debug.LogWarning("Mods: no glow template (GoF2 > Build > Mod Assets)"); return null; }
            var m = Copy(a.glow, name);
            float k = e.emissionIntensity > 0f ? e.emissionIntensity : 1f;
            var c = Rgb(e.emissionColor, Rgb(e.color, Color.white)) * k; c.a = 1f;
            m.SetColor("_EmissionColor", c);
            var tex = Texture(mod, e.emission ?? e.diffuse, false);
            if (tex != null) { tex.wrapMode = TextureWrapMode.Repeat; m.SetTexture("_EmissionMap", tex); }
            if (e.scroll != null && e.scroll.Length >= 2) m.SetVector("_Scroll", new Vector4(e.scroll[0], e.scroll[1], 0f, 0f));
            m.SetFloat("_UseVertexColor", e.ignoreVertexColors ? 0f : 1f);
            var g = new ModGlbImages.Glow { billboard = e.billboard };
            if (e.angleFade != null && e.angleFade.Length >= 2) g.angleFade = new Vector2(e.angleFade[0], e.angleFade[1]);
            if (e.pulse != null && e.pulse.Length >= 3)
            {
                g.pulseFrequency = e.pulse[0]; g.pulseMin = e.pulse[1]; g.pulseMax = e.pulse[2];
                g.pulseThreshold = e.pulse.Length > 3 ? e.pulse[3] : 0f;
                g.pulseStep = e.pulse.Length > 4 && e.pulse[4] > 0.5f; g.pulseSmooth = e.pulse.Length > 5 && e.pulse[5] > 0.5f;
                g.pulseVertexOffset = e.pulse.Length > 6 && e.pulse[6] > 0.5f;
            }
            GlowTerms(m, g);
            return m;
        }

        static readonly int FxAngleId = Shader.PropertyToID("_FxAngle"), FxPulseId = Shader.PropertyToID("_FxPulse"),
            FxPulseModeId = Shader.PropertyToID("_FxPulseMode"), BillboardId = Shader.PropertyToID("_Billboard");

        /// <summary>A glow layer's GoF3 effect terms on its GoF2/ModGlow material: the angle fade (_FxAngle: on, min, max,
        /// absolute), the pulse (_FxPulse: frequency, threshold, min, max; _FxPulseMode: on, step, smooth, vertex offset) and
        /// the billboard (_Billboard: its sprites face the camera, UV1 their corners' view-space offsets).</summary>
        internal static void GlowTerms(Material m, ModGlbImages.Glow g)
        {
            m.SetVector(FxAngleId, g.angleFade is Vector2 af ? new Vector4(1f, af.x, Mathf.Max(af.y, af.x + 1e-4f), g.angleAbsolute ? 1f : 0f) : Vector4.zero);
            m.SetVector(FxPulseId, new Vector4(g.pulseFrequency, g.pulseThreshold, g.pulseMin, g.pulseMax));
            m.SetVector(FxPulseModeId, new Vector4(g.pulseFrequency != 0f ? 1f : 0f, g.pulseStep ? 1f : 0f, g.pulseSmooth ? 1f : 0f, g.pulseVertexOffset ? 1f : 0f));
            m.SetFloat(BillboardId, g.billboard ? 1f : 0f);
        }

        /// <summary>A glTF material whose "extras" say "additive": true (ModGlbImages.Glow: the GoF3 importer's effect layers,
        /// their colour baked into the texture) as a GoF2/ModGlow copy: its emissive texture (else its base colour texture) x
        /// "intensity", its vertex colours, scroll, angle fade, pulse and billboard (GlowTerms).</summary>
        public static Material GlowFromGltf(GLTFast.Schema.MaterialBase g, GLTFast.IGltfReadable gltf,
            System.Func<int, ModGlbImages.Role, float, float, Texture2D> images, ModGlbImages.Glow glow)
        {
            var a = ModAssets.Get();
            if (a == null || a.glow == null) { Debug.LogWarning("Mods: no glow template (GoF2 > Build > Mod Assets)"); return null; }
            var m = Copy(a.glow, string.IsNullOrEmpty(g.name) ? "glTF glow" : g.name);
            var info = g.EmissiveTexture != null && g.EmissiveTexture.index >= 0 ? g.EmissiveTexture : g.PbrMetallicRoughness?.BaseColorTexture;
            if (info != null && info.index >= 0)
            {
                var t = images?.Invoke(info.index, ModGlbImages.Role.Color, 0f, 1f) ?? gltf.GetTexture(info.index);
                if (t != null)
                {
                    m.SetTexture("_EmissionMap", t);
                    if (gltf.IsTextureYFlipped(info.index)) { m.SetTextureScale("_EmissionMap", new Vector2(1f, -1f)); m.SetTextureOffset("_EmissionMap", new Vector2(0f, 1f)); }
                }
            }
            var c = Color.white * Mathf.Max(0f, glow.intensity); c.a = 1f;
            m.SetColor("_EmissionColor", c);
            m.SetVector("_Scroll", new Vector4(glow.scroll.x, glow.scroll.y, 0f, 0f));
            m.SetFloat("_UseVertexColor", glow.vertexColors ? 1f : 0f);
            GlowTerms(m, glow);
            return m;
        }

        /// <summary>A "shimmer" entry: a copy of GoF2/ModNozzleShimmer in the entry's colour (emissionColor x emissionIntensity,
        /// the kit colour slots' emission target too), its glow pattern (emission) and noise (flameNoise).</summary>
        static Material Shimmer(ModInfo mod, CustomShipMaterial e, string name, ModAssets a)
        {
            if (a.nozzleShimmer == null) { Debug.LogWarning("Mods: no nozzle shimmer template (GoF2 > Build > Mod Assets)"); return null; }
            var m = Copy(a.nozzleShimmer, name);
            float k = e.emissionIntensity > 0f ? e.emissionIntensity : 1f;
            var c = Rgb(e.emissionColor, new Color(1f, 0.3f, 0.05f)) * k; c.a = 1f;
            m.SetColor("_EmissionColor", c);
            var pattern = Texture(mod, e.emission, false);
            if (pattern != null) m.SetTexture("_EmissionMap", pattern);
            var noise = Texture(mod, e.flameNoise, true);
            if (noise != null) { noise.wrapMode = TextureWrapMode.Repeat; m.SetTexture("_NoiseMap", noise); }
            return m;
        }

        /// <summary>A glTF material as a URP Lit copy (ModGltfMaterials). 'images' gives a texture index's texture for a role
        /// when the mods' loader made it (ModGltf: compressed, metallic-roughness and normal maps already converted); else
        /// glTFast's own texture, converted here on the GPU.</summary>
        public static Material FromGltf(GLTFast.Schema.MaterialBase g, GLTFast.IGltfReadable gltf,
            System.Func<int, ModGlbImages.Role, float, float, Texture2D> images = null, Vector4? gof3 = null, Texture2D gof3Env = null)
        {
            var a = ModAssets.Get();
            if (a == null) return null;
            var mode = g.GetAlphaMode();
            bool clip = mode == GLTFast.Schema.MaterialBase.AlphaMode.Mask, glass = mode == GLTFast.Schema.MaterialBase.AlphaMode.Blend;
            var sgx = g.Extensions?.KHR_materials_pbrSpecularGlossiness;
            var m = Copy(a.Lit(clip, glass, false, sgx != null), string.IsNullOrEmpty(g.name) ? "glTF material" : g.name);
            bool ours = false;   // the last Tex came from 'images'
            var pbr = g.PbrMetallicRoughness;
            float metal = pbr != null ? Mathf.Clamp01(pbr.metallicFactor) : 0f, rough = pbr != null ? Mathf.Clamp01(pbr.roughnessFactor) : 1f;
            Texture2D Tex(GLTFast.Schema.TextureInfoBase info, string property, ModGlbImages.Role role = ModGlbImages.Role.Color)
            {
                ours = false;
                if (info == null || info.index < 0) return null;
                var t = images?.Invoke(info.index, role, metal, rough);
                ours = t != null;
                if (t == null) t = gltf.GetTexture(info.index);
                if (t != null && gltf.IsTextureYFlipped(info.index))
                {
                    m.SetTextureScale(property, new Vector2(1f, -1f));
                    m.SetTextureOffset(property, new Vector2(0f, 1f));
                }
                return t;
            }
            var baseColor = pbr != null ? pbr.BaseColor : Color.white;
            m.SetColor("_BaseColor", baseColor);
            var baseMap = pbr != null ? Tex(pbr.BaseColorTexture, "_BaseMap") : null;
            m.SetTexture("_BaseMap", baseMap);
            m.SetTexture("_MainTex", baseMap);
            var mr = pbr != null && sgx == null ? Tex(pbr.MetallicRoughnessTexture, "_MetallicGlossMap", ModGlbImages.Role.MetallicRoughness) : null;
            if (sgx != null)
            {
                // KHR_materials_pbrSpecularGlossiness (Galaxy on Fire 3's hulls): URP's specular setup. Its diffuse
                // is the base colour; the specular-glossiness map (RGB specular, sRGB; A glossiness) goes in as it is,
                // its factors as _SpecColor (x the map) and _Smoothness.
                var diffuse = Tex(sgx.diffuseTexture, "_BaseMap");
                if (diffuse != null) { m.SetTexture("_BaseMap", diffuse); m.SetTexture("_MainTex", diffuse); }
                if (sgx.diffuseFactor != null && sgx.diffuseFactor.Length >= 4)
                    m.SetColor("_BaseColor", new Color(sgx.diffuseFactor[0], sgx.diffuseFactor[1], sgx.diffuseFactor[2], sgx.diffuseFactor[3]));
                var sg = Tex(sgx.specularGlossinessTexture, "_SpecGlossMap");
                var f = sgx.specularFactor != null && sgx.specularFactor.Length >= 3
                    ? new Color(sgx.specularFactor[0], sgx.specularFactor[1], sgx.specularFactor[2], 1f) : Color.white;
                // (glTF's factor is linear; SetColor takes sRGB and linearises it)
                if (sg != null) { m.SetTexture("_SpecGlossMap", sg); m.SetColor("_SpecColor", f.gamma); }
                else { m.SetTexture("_SpecGlossMap", Solid(f.gamma, false)); m.SetColor("_SpecColor", Color.white); }
                m.SetFloat("_Smoothness", sgx.glossinessFactor);
                m.SetVector(GoF3ParamsId, gof3 ?? ModGlbImages.GoF3Params(1f, 1f, 1f, false));   // its "extras" (ModGlbImages)
                GoF3Environment(m, gof3Env);
            }
            else if (mr != null && ours)
            {
                m.SetTexture("_MetallicGlossMap", mr);
                m.SetFloat("_Smoothness", 1f);
            }
            else if (mr != null && a.metallicRoughness != null)
            {
                // B metal, G roughness -> R metal, A smoothness, the glTF factors applied (metallic = factor x B, roughness =
                // factor x G; URP Lit has no metallic multiplier for its map). A copy of the template, so the asset isn't changed.
                var rt = new RenderTexture(mr.width, mr.height, 0, RenderTextureFormat.ARGB32, RenderTextureReadWrite.Linear)
                    { name = m.name + " metallic", useMipMap = true, autoGenerateMips = true, wrapMode = mr.wrapMode };
                if (mrBlit == null) mrBlit = new Material(a.metallicRoughness);
                mrBlit.SetFloat("_MetallicFactor", metal);
                mrBlit.SetFloat("_RoughnessFactor", rough);
                Graphics.Blit(mr, rt, mrBlit);
                made.Add(rt);
                m.SetTexture("_MetallicGlossMap", rt);
                m.SetFloat("_Smoothness", 1f);
            }
            else
            {
                m.SetTexture("_MetallicGlossMap", Solid(new Color(metal, 0f, 0f, 1f)));
                m.SetFloat("_Smoothness", 1f - rough);
            }
            Texture nrm = Tex(g.NormalTexture, "_BumpMap", ModGlbImages.Role.Normal);
            if (nrm != null && NormalsInAlpha && !ours) nrm = NormalToAlpha(nrm, m.name);
            m.SetTexture("_BumpMap", nrm);
            if (g.NormalTexture != null) m.SetFloat("_BumpScale", g.NormalTexture.scale);
            var emissive = g.Emissive;
            var emMap = Tex(g.EmissiveTexture, "_EmissionMap");
            if (emMap != null || emissive.maxColorComponent > 0f)
            {
                m.SetTexture("_EmissionMap", emMap);
                m.SetColor("_EmissionColor", emMap != null && emissive.maxColorComponent <= 0f ? Color.white : emissive);
                Glowing(m);
            }
            else m.SetColor("_EmissionColor", Color.black);
            if (clip) m.SetFloat("_Cutoff", g.alphaCutoff);
            if (g.doubleSided) m.SetFloat("_Cull", (float)CullMode.Off);
            return m;
        }

        static Material mrBlit;

        /// <summary>A mod's hangar or bar: its materials (copies of the GoF2/Hull templates) back on URP Lit, like the game's
        /// rooms, which don't have the original's hull reflection. The keywords are enabled again on the new shader: a shader
        /// switch keeps only those both declare alike, and URP Lit's _EMISSION was dropped, so every mod room lost its glow.</summary>
        public static void PlainLit(GameObject room)
        {
            var lit = Shader.Find("Universal Render Pipeline/Lit");
            if (lit == null || room == null) return;
            foreach (var r in room.GetComponentsInChildren<Renderer>(true))
                foreach (var m in r.sharedMaterials)
                    if (m != null && m.shader != null && m.shader.name == "GoF2/Hull")
                    {
                        int q = m.renderQueue;
                        var keywords = m.shaderKeywords;
                        m.shader = lit;
                        foreach (var k in keywords) m.EnableKeyword(k);
                        m.renderQueue = q;
                    }
        }

        /// <summary>Material for renderers without one.</summary>
        public static Material Default()
        {
            var a = ModAssets.Get();
            if (a == null) return null;
            var m = Copy(a.litOpaque, "mod default");
            m.SetColor("_EmissionColor", Color.black);
            m.SetTexture("_BumpMap", null);
            m.SetTexture("_MetallicGlossMap", Solid(new Color(0f, 0f, 0f, 0.5f)));
            return m;
        }
    }
}
