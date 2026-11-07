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

        /// <summary>A material with an emission map or colour draws it: _EMISSION on its copy, whatever the template holds. The
        /// templates' keyword is lost whenever the Editor re-saves them (a shader reimport did, 2026-11-02: every mod room and
        /// glow went dark, the Manticore's hangar a flat grey with GoF3's baked lighting in its emission); ModAssetsGuard
        /// only restores it before a build.</summary>
        static void Glowing(Material m)
        {
            m.EnableKeyword("_EMISSION");
            m.globalIlluminationFlags = MaterialGlobalIlluminationFlags.None;
        }

        public static Material FromSpec(ModInfo mod, CustomShipMaterial e, string name)
        {
            var a = ModAssets.Get();
            if (a == null) return null;
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
            }
            else
            {
                var ms = Texture(mod, e.metallicSmoothness, true);
                m.SetTexture("_MetallicGlossMap", ms != null ? ms : Solid(new Color(e.metallic >= 0f ? e.metallic : 0f, 0f, 0f, 1f)));
            }
            m.SetFloat("_Smoothness", e.smoothness);
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
            System.Func<int, ModGlbImages.Role, float, float, Texture2D> images = null, Vector4? gof3 = null)
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
