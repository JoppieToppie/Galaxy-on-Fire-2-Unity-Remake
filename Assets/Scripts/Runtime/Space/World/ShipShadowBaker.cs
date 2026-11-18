// ShipShadowBaker.cs
// Remake: a hangar ship's floor shadow map (ShipShadowSet's format, HangarShipShadow), made from its hull triangles seen
// from above: rasterised into a 128 x 128 map over a square 2.2 x the hull, R = the hull's underside (the lowest hull
// point over each texel above the hull's bottom, / heightRange; spread outward to the texels around it), G = the
// silhouette (lightly blurred), A = a wide soft halo of it (box blur, 3 passes). The Editor's "GoF2 > Build > Hangar
// Shadows" (HangarShadowsBuilder) bakes the game's ships with it; at run time it bakes the ships the set has no entry for
// whose meshes are readable (the mods' ships: glTFast keeps its meshes readable), once per assembly and set of mods, the
// rasterising on a worker thread (Request / Bake). The game's own meshes are imported non-readable, so the debug capital
// hulls keep the soft oval. A build without the originals builds the game's ships from the player's converted copy with
// readable meshes (OriginalModels): their maps are baked here too, kept in that copy's GPU caches (GameContent.GpuCache/
// shadows, by the same hull hash), so each is baked once.
// A mod ship's map is kept on disk for later plays (persistentDataPath/ModCache/ShipShadows/<mod id>/<hull hash>.shadow,
// read and written on the worker thread): the hull hash is the ship's identity (its triangles: a changed model is another
// file; their "ship_NNN_mod" numbers differ between games). The loading screen's pass keeps only the files its ships used
// in their mods' folders (a new version's old maps go), and ModManager.Scan deletes the folders of the mods no longer
// installed (PruneCache).
// The mods' ships are prepared on the main menu's loading screen (ModShipsBaked, polled by ModLoading): once their models
// are built, every template's map is read from the cache or baked, one hull read per frame, so a hangar never waits.

using System.Collections.Concurrent;
using System.Collections.Generic;
using System.IO;
using System.Security.Cryptography;
using System.Threading.Tasks;
using UnityEngine;

namespace GoF2Remake.World
{
    [Unity.Scripting.LifecycleManagement.NoAutoStaticsCleanup]
    public static class ShipShadowBaker
    {
        public const int N = 128;
        const int HaloRadius = 13, HaloPasses = 3;
        const float Margin = 2.2f;
        /// <summary>Bumped when the bake's output changes: older cached maps are then baked again.</summary>
        const int CacheVersion = 1;
        const int CacheMagic = 0x31485347;   // "GSH1"

        /// <summary>A baked map: the pixels (linear RGBA) and where they lie in the ship's space (Unity metres, scale 1).</summary>
        public sealed class Result
        {
            public Color32[] pixels;
            public Vector2 center;
            public float size, bottom, heightRange;
        }

        // ---- run time: ships without a baked entry ----------------------------------------------------------------

        static readonly Dictionary<string, Task<Result>> pending = new Dictionary<string, Task<Result>>();
        static readonly Dictionary<string, ShipShadowSet.Entry> baked = new Dictionary<string, ShipShadowSet.Entry>();
        /// <summary>The cache file each assembly's map came from or went to (written by the worker threads).</summary>
        static readonly ConcurrentDictionary<string, string> cacheFiles = new ConcurrentDictionary<string, string>();
        static int revision = -1;

        [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.SubsystemRegistration)]
        static void ResetStatics()
        {
            pending.Clear(); baked.Clear(); cacheFiles.Clear(); revision = -1;
            prebake.Clear(); prebakeRevision = -1; prebakeNext = 0; PrebakeDone = 0; PrebakeCurrent = null;
        }

        /// <summary>The mods changed: a ship type's model may be another one now.</summary>
        static void Check()
        {
            if (revision == Modding.ModManager.Revision) return;
            foreach (var e in baked.Values) if (e?.texture != null) Object.Destroy(e.texture);
            baked.Clear();
            pending.Clear();
            cacheFiles.Clear();
            revision = Modding.ModManager.Revision;
        }

        /// <summary>A mod's ship (ModShipBuilder's template: origin "mod <id>"): baked here, never from the set.</summary>
        public static bool IsModShip(GameObject ship)
        {
            var asm = ship != null ? ship.GetComponent<Visuals.AssembledObject>() : null;
            return asm != null && asm.origin != null && asm.origin.StartsWith("mod ");
        }

        /// <summary>The run-time map of 'assembly' (null: none yet). The first call for a ship type with readable meshes
        /// starts its bake ('ship' an instance of it); false = it can't be baked here (non-readable meshes or no hull),
        /// so the caller keeps the oval.</summary>
        public static bool Request(string assembly, GameObject ship, out ShipShadowSet.Entry entry)
        {
            Check();
            entry = null;
            if (string.IsNullOrEmpty(assembly)) return false;
            if (baked.TryGetValue(assembly, out entry)) return entry != null;
            if (pending.TryGetValue(assembly, out var task))
            {
                if (!task.IsCompleted) return true;
                pending.Remove(assembly);
                entry = task.Status == TaskStatus.RanToCompletion ? ToEntry(assembly, task.Result) : null;
                if (task.IsFaulted) Debug.LogWarning($"Hangar shadow of {assembly}: {task.Exception?.GetBaseException().Message}");
                baked[assembly] = entry;
                return entry != null;
            }
            var tris = new List<Vector3>();
            if (!Collect(ship, tris, out float bottom, false)) { baked[assembly] = null; return false; }
            var mod = ModOf(ship);
            // a game ship built from the player's converted copy (its meshes are readable): kept with that copy's GPU caches
            string dir = mod != null ? Path.Combine(CacheRoot, mod.Id)
                       : !IsModShip(ship) && Data.GameContent.Installed ? Path.Combine(Data.GameContent.GpuCache, "shadows") : null;
            pending[assembly] = Task.Run(() => BakeCached(assembly, tris, bottom, mod, dir));
            return true;
        }

        // ---- the loading screen: every mod ship prepared before a game starts --------------------------------------

        static readonly List<(string assembly, string name, GameObject template)> prebake = new List<(string, string, GameObject)>();
        static int prebakeRevision = -1, prebakeNext;

        /// <summary>The mod ships whose maps are ready (ModLoading's progress and line).</summary>
        public static int PrebakeDone { get; private set; }
        public static int PrebakeCount => prebake.Count;
        /// <summary>The ship whose map is being made now (null: none).</summary>
        public static string PrebakeCurrent { get; private set; }

        /// <summary>The loading screen has hangar shadows to make: the option shows them, and this isn't a server.</summary>
        public static bool PrebakeWanted => Data.Settings.HangarShadows != Data.Settings.HangarShadowsOff && !Application.isBatchMode;

        /// <summary>How far the mod ships' maps are, 0..1 (0 until their models are built).</summary>
        public static float PrebakeProgress =>
            prebakeRevision != Modding.ModManager.Revision ? 0f : prebake.Count == 0 ? 1f : PrebakeDone / (float)prebake.Count;

        /// <summary>Every active mod ship's map is ready (read from the cache or baked). Polled each frame while the
        /// loading screen shows (ModLoading.Busy): starts once the models are built, then reads one more hull per call and
        /// collects the finished bakes.</summary>
        public static bool ModShipsBaked
        {
            get
            {
                if (!Modding.ModShips.Ready) return false;
                if (prebakeRevision != Modding.ModManager.Revision) StartPrebake();
                if (PrebakeDone >= prebake.Count) return true;
                TickPrebake();
                return PrebakeDone >= prebake.Count;
            }
        }

        static void StartPrebake()
        {
            prebakeRevision = Modding.ModManager.Revision;
            prebake.Clear();
            prebakeNext = 0;
            PrebakeDone = 0;
            PrebakeCurrent = null;
            if (!PrebakeWanted) return;
            foreach (var (_, name, template) in Modding.ModShips.Built) prebake.Add((template.name, name, template));
        }

        const double PrebakeReadMs = 3.0;

        static void TickPrebake()
        {
            // The hull reads (main thread) for up to PrebakeReadMs of the frame, at least one; the bakes run on worker threads
            // meanwhile. One ship per frame left a mod of 79 ships most of a second after its models were built (a read takes
            // under a millisecond on average).
            long readStart = System.Diagnostics.Stopwatch.GetTimestamp();
            while (prebakeNext < prebake.Count)
            {
                var next = prebake[prebakeNext++];
                if (next.template != null) Request(next.assembly, next.template, out _);
                if ((System.Diagnostics.Stopwatch.GetTimestamp() - readStart) * 1000.0 / System.Diagnostics.Stopwatch.Frequency >= PrebakeReadMs) break;
            }
            int done = 0;
            string current = null;
            for (int i = 0; i < prebakeNext; i++)
            {
                var p = prebake[i];
                if (p.template == null || !Request(p.assembly, p.template, out var entry) || entry != null) done++;
                else current ??= p.name;
            }
            PrebakeDone = done;
            PrebakeCurrent = current ?? (prebakeNext < prebake.Count ? prebake[prebakeNext].name : null);
            if (PrebakeDone >= prebake.Count && prebake.Count > 0)
            {
                Debug.Log($"Hangar shadows: {prebake.Count} mod ship(s) ready");
                PruneUnused();
            }
        }

        /// <summary>The pass is done: in each of its mods' folders only the files its ships used stay (an older version's
        /// maps, other files and folders go).</summary>
        static void PruneUnused()
        {
            var used = new HashSet<string>(cacheFiles.Values, System.StringComparer.OrdinalIgnoreCase);
            var dirs = new HashSet<string>(System.StringComparer.OrdinalIgnoreCase);
            foreach (var p in prebake)
            {
                var mod = p.template != null ? ModOf(p.template) : null;
                if (mod != null) dirs.Add(Path.GetFullPath(Path.Combine(CacheRoot, mod.Id)));
            }
            int removed = 0;
            foreach (var dir in dirs)
                try
                {
                    if (!Directory.Exists(dir)) continue;
                    foreach (var f in Directory.GetFiles(dir))
                        if (!used.Contains(Path.GetFullPath(f))) { File.Delete(f); removed++; }
                    foreach (var d in Directory.GetDirectories(dir)) { Directory.Delete(d, true); removed++; }
                }
                catch (System.Exception e) { Debug.LogWarning($"Hangar shadow cache: {e.Message}"); }
            if (removed > 0) Debug.Log($"Hangar shadows: removed {removed} unused cached map(s)");
        }

        // ---- the disk cache of the mods' ships ------------------------------------------------------------------------

        /// <summary>persistentDataPath/ModCache/ShipShadows (main thread only: persistentDataPath).</summary>
        static string CacheRoot => Path.Combine(GoF2Remake.GamePaths.Data, "ModCache", "ShipShadows");

        /// <summary>The installed mod a ship comes from (its template's origin "mod &lt;id&gt;"), else null.</summary>
        static Modding.ModInfo ModOf(GameObject ship)
        {
            if (!IsModShip(ship)) return null;
            var mod = Modding.ModManager.Find(ship.GetComponent<Visuals.AssembledObject>().origin.Substring(4).Trim());
            return mod != null && mod.Manifest != null && !string.IsNullOrEmpty(mod.Id) ? mod : null;
        }

        /// <summary>The map from the cache, else baked and saved there (a worker thread; 'dir' null: not cached; 'mod' null:
        /// a game ship of the converted copy, cached in its GPU caches).</summary>
        static Result BakeCached(string assembly, List<Vector3> tris, float bottom, Modding.ModInfo mod, string dir)
        {
            if (dir == null) return Rasterise(tris, bottom);
            string label = mod != null ? mod.Id : "the game's ships";
            string file = null;
            try
            {
                file = Path.GetFullPath(Path.Combine(dir, HullHash(tris, bottom) + ".shadow"));
                var cached = Read(file);
                if (cached != null) { cacheFiles[assembly] = file; return cached; }
            }
            catch (System.Exception e) { Debug.LogWarning($"Hangar shadow cache ({label}): {e.Message}"); }
            var r = Rasterise(tris, bottom);
            if (r != null && file != null)
                try
                {
                    Directory.CreateDirectory(dir);
                    Write(file, r);
                    cacheFiles[assembly] = file;
                }
                catch (System.Exception e) { Debug.LogWarning($"Hangar shadow cache ({label}): {e.Message}"); }
            return r;
        }

        /// <summary>The hull's identity: SHA-256 of its triangles, its bottom, the map size and CacheVersion (first 16 hex).</summary>
        static string HullHash(List<Vector3> tris, float bottom)
        {
            var bytes = new byte[(tris.Count * 3 + 3) * 4];
            int o = 0;
            void Put(float f) { System.BitConverter.TryWriteBytes(new System.Span<byte>(bytes, o, 4), f); o += 4; }
            foreach (var p in tris) { Put(p.x); Put(p.y); Put(p.z); }
            Put(bottom); Put(N); Put(CacheVersion);
            using var sha = SHA256.Create();
            var h = sha.ComputeHash(bytes);
            var sb = new System.Text.StringBuilder(16);
            for (int i = 0; i < 8; i++) sb.Append(h[i].ToString("x2"));
            return sb.ToString();
        }

        static Result Read(string file)
        {
            if (!File.Exists(file)) return null;
            using var r = new BinaryReader(File.OpenRead(file));
            if (r.BaseStream.Length != 4 * 8 + N * N * 4 || r.ReadInt32() != CacheMagic || r.ReadInt32() != CacheVersion || r.ReadInt32() != N) return null;
            var result = new Result { center = new Vector2(r.ReadSingle(), r.ReadSingle()), size = r.ReadSingle(), bottom = r.ReadSingle(), heightRange = r.ReadSingle() };
            var bytes = r.ReadBytes(N * N * 4);
            result.pixels = new Color32[N * N];
            for (int i = 0; i < result.pixels.Length; i++)
                result.pixels[i] = new Color32(bytes[i * 4], bytes[i * 4 + 1], bytes[i * 4 + 2], bytes[i * 4 + 3]);
            return result.size > 0f ? result : null;
        }

        static void Write(string file, Result r)
        {
            string tmp = file + ".tmp";
            using (var w = new BinaryWriter(File.Create(tmp)))
            {
                w.Write(CacheMagic); w.Write(CacheVersion); w.Write(N);
                w.Write(r.center.x); w.Write(r.center.y); w.Write(r.size); w.Write(r.bottom); w.Write(r.heightRange);
                foreach (var c in r.pixels) { w.Write(c.r); w.Write(c.g); w.Write(c.b); w.Write(c.a); }
            }
            if (File.Exists(file)) File.Delete(file);
            File.Move(tmp, file);
        }

        /// <summary>Deletes the cached maps of the mods no longer installed (ModManager.Scan: 'installedIds' every mod id
        /// found, on or off).</summary>
        public static void PruneCache(IEnumerable<string> installedIds)
        {
            try
            {
                string root = CacheRoot;
                if (!Directory.Exists(root)) return;
                var keep = new HashSet<string>(installedIds, System.StringComparer.OrdinalIgnoreCase);
                foreach (var dir in Directory.GetDirectories(root))
                    if (!keep.Contains(Path.GetFileName(dir)))
                    {
                        Directory.Delete(dir, true);
                        Debug.Log($"Hangar shadows: removed the cached maps of {Path.GetFileName(dir)} (no longer installed)");
                    }
            }
            catch (System.Exception e) { Debug.LogWarning($"Hangar shadow cache: {e.Message}"); }
        }

        static ShipShadowSet.Entry ToEntry(string assembly, Result r)
        {
            if (r == null) return null;
            return new ShipShadowSet.Entry
            {
                assembly = assembly, texture = ToTexture(r, assembly + " shadow"),
                center = r.center, size = r.size, bottom = r.bottom, heightRange = r.heightRange,
            };
        }

        /// <summary>The map as the shader reads it: linear, uncompressed, clamped, no mips.</summary>
        public static Texture2D ToTexture(Result r, string name)
        {
            var tex = new Texture2D(N, N, TextureFormat.RGBA32, false, true) { name = name, wrapMode = TextureWrapMode.Clamp };
            tex.SetPixels32(r.pixels);
            tex.Apply(false, !Application.isEditor || Application.isPlaying);
            return tex;
        }

        // ---- the bake ---------------------------------------------------------------------------------------------

        /// <summary>The hull's triangles in the ship's own space (scale 1) and its lowest point: every active, enabled mesh
        /// but the additive layers, the LOD meshes and the mod ships' engine / throttle glows. 'anyMesh': read non-readable
        /// meshes too (the Editor can); else false when one of them isn't readable (a build can't).</summary>
        public static bool Collect(GameObject ship, List<Vector3> tris, out float bottom, bool anyMesh)
        {
            bottom = float.PositiveInfinity;
            if (ship == null) return false;
            var root = ship.transform.worldToLocalMatrix;
            var vertices = new List<Vector3>();
            var triangles = new List<int>();
            // The parts switched on under the ship's root, whatever the root's own state: the mods' templates sit under an
            // inactive holder (ModShips) and are baked there on the loading screen.
            foreach (var mf in ship.GetComponentsInChildren<MeshFilter>(true))
            {
                var mesh = mf.sharedMesh;
                if (mesh == null || !ActiveUnder(mf.transform, ship.transform)) continue;
                string n = mf.name;
                if (n.Contains("_add") || n.Contains("_lod") || n.StartsWith("engine_glow") || n.StartsWith("throttle_glow")) continue;
                var mr = mf.GetComponent<MeshRenderer>();
                if (mr == null || !mr.enabled) continue;
                if (!mesh.isReadable && !anyMesh) return false;
                var m = root * mf.transform.localToWorldMatrix;
                mesh.GetVertices(vertices);
                int start = tris.Count;
                for (int s = 0; s < mesh.subMeshCount; s++)
                {
                    var topology = mesh.GetTopology(s);
                    if (topology != MeshTopology.Triangles) continue;
                    mesh.GetTriangles(triangles, s);
                    foreach (int i in triangles) tris.Add(m.MultiplyPoint3x4(vertices[i]));
                }
                for (int i = start; i < tris.Count; i++) bottom = Mathf.Min(bottom, tris[i].y);
            }
            return tris.Count > 0;
        }

        /// <summary>'t' and its parents up to (not including) 'root' are all switched on.</summary>
        static bool ActiveUnder(Transform t, Transform root)
        {
            for (; t != null && t != root; t = t.parent) if (!t.gameObject.activeSelf) return false;
            return true;
        }

        /// <summary>The map of the triangles (thread-safe: no Unity objects).</summary>
        public static Result Rasterise(List<Vector3> tris, float bottom)
        {
            if (tris == null || tris.Count < 3) return null;
            var min = new Vector2(float.PositiveInfinity, float.PositiveInfinity);
            var max = new Vector2(float.NegativeInfinity, float.NegativeInfinity);
            foreach (var p in tris)
            {
                min = Vector2.Min(min, new Vector2(p.x, p.z));
                max = Vector2.Max(max, new Vector2(p.x, p.z));
            }
            var c = (min + max) * 0.5f;
            float size = Mathf.Max(max.x - min.x, max.y - min.y) * Margin;
            if (size <= 0f) return null;
            float cell = size / N;
            var origin = c - new Vector2(size, size) * 0.5f;
            // The lowest hull point over each texel (infinity: no hull).
            var under = new float[N * N];
            for (int i = 0; i < under.Length; i++) under[i] = float.PositiveInfinity;
            Vector3 Grid(Vector3 p) => new Vector3((p.x - origin.x) / cell, p.y, (p.z - origin.y) / cell);
            for (int i = 0; i + 2 < tris.Count; i += 3) Fill(under, Grid(tris[i]), Grid(tris[i + 1]), Grid(tris[i + 2]));
            var core = new float[N * N];
            float top = bottom;
            for (int i = 0; i < under.Length; i++) if (!float.IsInfinity(under[i])) { core[i] = 1f; top = Mathf.Max(top, under[i]); }
            var halo = (float[])core.Clone();
            for (int pass = 0; pass < HaloPasses; pass++) { Blur(halo, true, HaloRadius); Blur(halo, false, HaloRadius); }
            Blur(core, true, 1);
            Blur(core, false, 1);
            Spread(under);
            float range = Mathf.Max(1f, top - bottom);
            var px = new Color32[N * N];
            for (int i = 0; i < px.Length; i++)
                px[i] = new Color32(Byte((under[i] - bottom) / range), Byte(core[i]), 0, Byte(halo[i]));
            return new Result { pixels = px, center = c, size = size, bottom = bottom, heightRange = range };
        }

        static byte Byte(float v) => (byte)Mathf.RoundToInt(Mathf.Clamp01(v) * 255f);

        /// <summary>Fills a triangle (x / z in grid units, y in metres) into the map: the cells whose centres it covers keep
        /// the lowest height of it there.</summary>
        static void Fill(float[] under, Vector3 a3, Vector3 b3, Vector3 c3)
        {
            Vector2 a = new Vector2(a3.x, a3.z), b = new Vector2(b3.x, b3.z), c = new Vector2(c3.x, c3.z);
            float area = (b.x - a.x) * (c.y - a.y) - (b.y - a.y) * (c.x - a.x);
            if (Mathf.Abs(area) < 1e-6f) return;
            int x0 = Mathf.Max(0, Mathf.FloorToInt(Mathf.Min(a.x, Mathf.Min(b.x, c.x)))), x1 = Mathf.Min(N - 1, Mathf.CeilToInt(Mathf.Max(a.x, Mathf.Max(b.x, c.x))));
            int y0 = Mathf.Max(0, Mathf.FloorToInt(Mathf.Min(a.y, Mathf.Min(b.y, c.y)))), y1 = Mathf.Min(N - 1, Mathf.CeilToInt(Mathf.Max(a.y, Mathf.Max(b.y, c.y))));
            float s = Mathf.Sign(area);
            for (int y = y0; y <= y1; y++)
            for (int x = x0; x <= x1; x++)
            {
                var p = new Vector2(x + 0.5f, y + 0.5f);
                float w0 = ((b.x - a.x) * (p.y - a.y) - (b.y - a.y) * (p.x - a.x)) * s;
                float w1 = ((c.x - b.x) * (p.y - b.y) - (c.y - b.y) * (p.x - b.x)) * s;
                float w2 = ((a.x - c.x) * (p.y - c.y) - (a.y - c.y) * (p.x - c.x)) * s;
                if (w0 < 0f || w1 < 0f || w2 < 0f) continue;
                // w1 weighs a (the edge opposite it), w2 b, w0 c.
                float h = (w1 * a3.y + w2 * b3.y + w0 * c3.y) / Mathf.Abs(area);
                if (h < under[y * N + x]) under[y * N + x] = h;
            }
        }

        /// <summary>Gives every texel without hull the height of the nearest one with it (a breadth-first spread).</summary>
        static void Spread(float[] under)
        {
            var queue = new Queue<int>();
            for (int i = 0; i < under.Length; i++) if (!float.IsInfinity(under[i])) queue.Enqueue(i);
            while (queue.Count > 0)
            {
                int i = queue.Dequeue(), x = i % N, y = i / N;
                void Try(int xx, int yy)
                {
                    if (xx < 0 || yy < 0 || xx >= N || yy >= N) return;
                    int j = yy * N + xx;
                    if (!float.IsInfinity(under[j])) return;
                    under[j] = under[i];
                    queue.Enqueue(j);
                }
                Try(x - 1, y); Try(x + 1, y); Try(x, y - 1); Try(x, y + 1);
            }
        }

        static void Blur(float[] mask, bool horizontal, int radius)
        {
            var copy = (float[])mask.Clone();
            for (int y = 0; y < N; y++)
            for (int x = 0; x < N; x++)
            {
                float sum = 0f;
                for (int k = -radius; k <= radius; k++)
                {
                    int xx = horizontal ? x + k : x, yy = horizontal ? y : y + k;
                    if (xx >= 0 && xx < N && yy >= 0 && yy < N) sum += copy[yy * N + xx];
                }
                mask[y * N + x] = sum / (2 * radius + 1);
            }
        }
    }
}
