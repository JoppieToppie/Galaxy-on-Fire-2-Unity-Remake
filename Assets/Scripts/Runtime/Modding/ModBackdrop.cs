// ModBackdrop.cs
// Remake mods: the planets and suns of the mods' stations and systems (stations.json "planetTexture", systems.json
// "sunTexture"): Backdrop's texture name "mod:<mod id>|<path>" makes a material like the game's own (a copy of
// planet_000_big / sun_000 in Resources/GoF2Backdrop, the GoF2/Backdrop shader) with the mod's PNG. The PNG is decoded in
// the background with the station models (Preload, ModStations), so building an orbit finds it made.
// backdrop.json (optional, the last mod in the load order that has one wins):
//   { "planets": "star", "planetGlow": 2, "planetFlare": 0.35 }
// draws the planets whose texture a mod replaces (textures/planet_000_small.png...) like stars: always facing the camera
// with its roll, never mirrored, no ring, their near-white core lifted to planetGlow under the remake's bloom, and the
// sun's flare at planetFlare of its strength (0 = none, 1 = the sun's) when looked at: a slight swell and a horizontal
// streak.

using System.Collections.Generic;
using System.Threading.Tasks;
using UnityEngine;

namespace GoF2Remake.Modding
{
    [Unity.Scripting.LifecycleManagement.NoAutoStaticsCleanup]
    public static class ModBackdrop
    {
        public const string Prefix = "mod:";

        static readonly Dictionary<string, Material> materials = new Dictionary<string, Material>();
        static int revision = -1;

        [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.SubsystemRegistration)]
        static void ResetStatics() { materials.Clear(); revision = -1; lookRevision = -1; starPlanets = false; planetGlow = 1f; planetFlare = 0f; }

        static int lookRevision = -1;
        static bool starPlanets;
        static float planetGlow = 1f, planetFlare;

        /// <summary>backdrop.json: the mods' planets drawn as stars, and their core glow under the remake's bloom.</summary>
        public static bool StarPlanets(out float glow) => StarPlanets(out glow, out _);

        /// <summary>backdrop.json, with the strength of the sun-like flare (0..1).</summary>
        public static bool StarPlanets(out float glow, out float flare)
        {
            if (lookRevision != ModManager.Revision)
            {
                lookRevision = ModManager.Revision;
                starPlanets = false;
                planetGlow = 1f;
                planetFlare = 0f;
                foreach (var mod in ModManager.Active)
                {
                    if (mod.Source == null || !mod.Source.Exists(LookFile)) continue;
                    try
                    {
                        if (!(ModJson.Read(mod.Source, LookFile) is Newtonsoft.Json.Linq.JObject o)) continue;
                        starPlanets = string.Equals(ModJson.Str(o, "planets"), "star", System.StringComparison.OrdinalIgnoreCase);
                        planetGlow = Mathf.Max(0f, ModJson.Float(o, "planetGlow", 2f, LookFile));
                        planetFlare = Mathf.Clamp01(ModJson.Float(o, "planetFlare", 0.35f, LookFile));
                    }
                    catch (System.Exception e) { Debug.LogWarning($"Mods: {mod.Id}: {LookFile}: {e.Message}"); }
                }
            }
            glow = planetGlow;
            flare = planetFlare;
            return starPlanets;
        }

        public const string LookFile = "backdrop.json";

        public static bool IsMod(string texture) => texture != null && texture.StartsWith(Prefix);

        static bool Split(string name, out ModInfo mod, out string path)
        {
            mod = null;
            path = null;
            if (!IsMod(name)) return false;
            int bar = name.IndexOf('|');
            if (bar < 0) return false;
            mod = ModManager.Find(name.Substring(Prefix.Length, bar - Prefix.Length));
            path = name.Substring(bar + 1);
            return mod != null;
        }

        /// <summary>Decodes the PNG in the background (ModMaterials' cache).</summary>
        public static Task Preload(string name) =>
            Split(name, out var mod, out var path) ? ModMaterials.PreloadTexture(mod, path, false) : Task.CompletedTask;

        /// <summary>The texture of a "mod:" name (a skybox image; null: unreadable), decoded on first use when Preload hasn't.</summary>
        public static Texture2D Texture(string name) => Split(name, out var mod, out var path) ? ModMaterials.Texture(mod, path, false) : null;

        /// <summary>The material for a "mod:" texture name; 'sun': like the suns (else like the planets). Null: unreadable.</summary>
        public static Material Material(string name, bool sun)
        {
            if (revision != ModManager.Revision) { foreach (var m in materials.Values) if (m != null) Object.Destroy(m); materials.Clear(); revision = ModManager.Revision; }
            if (materials.TryGetValue(name, out var mat)) return mat;
            if (!Split(name, out var mod, out var path)) return null;
            var tex = ModMaterials.Texture(mod, path, false);
            string templateName = sun ? "sun_000" : "planet_000_big";
            var template = Resources.Load<Material>($"GoF2Backdrop/{templateName}")
                           ?? GoF2Remake.Data.OriginalRefs.Find<Material>($"Resources/GoF2Backdrop/{templateName}.mat");
            if (tex == null || template == null) return materials[name] = null;
            tex.wrapMode = TextureWrapMode.Clamp;
            mat = new Material(template) { name = name };
            mat.SetTexture("_MainTex", tex);
            return materials[name] = mat;
        }
    }
}
