// ModWarp.cs
// Remake mods: the look and sound of the warp (Visuals.WarpSky). warp.json
// (optional, the last mod in the load order that has one wins):
//   { "tunnel": "warp/tunnel.png", "detail": "warp/detail.png", "streaks": "warp/streaks.png", "tint": "#8cd0ff",
//     "loop": "warp/loop.ogg", "enter": "warp/enter.ogg" }
// The sky itself is what stretches; the images shape it (their red channel, tiling): "tunnel" and "detail" break it into
// bands, "streaks" are the thin lines over it, in "tint". Each is optional, the game makes its own without.

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
            public string tunnel, detail, streaks, loop, enter;
            public Color? tint;
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
            var d = new Def
            {
                mod = mod, tunnel = ModJson.Str(o, "tunnel"), detail = ModJson.Str(o, "detail"), streaks = ModJson.Str(o, "streaks"),
                loop = ModJson.Str(o, "loop"), enter = ModJson.Str(o, "enter"),
            };
            if (ModJson.Str(o, "tint") is string tint)
            {
                if (ColorUtility.TryParseHtmlString(tint, out var c)) d.tint = c;
                else mod.Warnings.Add($"{File}: \"tint\" isn't a colour like \"#8cd0ff\" (ignored)");
            }
            return d;
        }

        /// <summary>A warp.json image, null when it can't be read.</summary>
        public static Texture2D Texture(Def d, string path) => ModMaterials.Texture(d.mod, path, true);
    }
}
