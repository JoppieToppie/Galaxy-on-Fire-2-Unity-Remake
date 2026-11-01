// ModWarp.cs
// Remake mods: the look and sound of the warp (Visuals.WarpSky). warp.json
// (optional, the last mod in the load order that has one wins):
//   { "tunnel": "warp/tunnel.png", "detail": "warp/detail.png", "loop": "warp/loop.ogg", "enter": "warp/enter.ogg" }
// The sky itself is what stretches; the images shape it (their red channel, tiling): "tunnel" and "detail" break it into
// bands. Each is optional, the game makes its own without.

using System;
using GoF2Remake.Data;
using UnityEngine;

namespace GoF2Remake.Modding
{
    [Unity.Scripting.LifecycleManagement.NoAutoStaticsCleanup]
    public static class ModWarp
    {
        public const string File = "warp.json";

        public sealed class Def
        {
            public ModInfo mod;
            public string tunnel, detail, loop, enter;
        }

        static Def current;
        static int revision = -1;

        [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.SubsystemRegistration)]
        static void ResetStatics() { current = null; revision = -1; }

        /// <summary>The active look, null without a mod's warp.json.</summary>
        public static Def Current
        {
            get
            {
                if (revision == ModManager.Revision) return current;
                revision = ModManager.Revision;
                current = null;
                foreach (var mod in ModManager.Active)
                {
                    if (mod.Source == null || !mod.Source.Exists(File)) continue;
                    try
                    {
                        if (ModJson.Read(mod.Source, File) is Newtonsoft.Json.Linq.JObject o) current = Parse(mod, o);
                    }
                    catch (Exception e) { Debug.LogWarning($"Mods: {mod.Id}: {File}: {e.Message}"); }
                }
                return current;
            }
        }

        static Def Parse(ModInfo mod, Newtonsoft.Json.Linq.JObject o)
        {
            return new Def
            {
                mod = mod, tunnel = ModJson.Str(o, "tunnel"), detail = ModJson.Str(o, "detail"),
                loop = ModJson.Str(o, "loop"), enter = ModJson.Str(o, "enter"),
            };
        }

        /// <summary>A warp.json image, null when it can't be read.</summary>
        public static Texture2D Texture(Def d, string path) => ModMaterials.Texture(d.mod, path, true);
    }
}
