// GameLods.cs
// Remake (AutoLods): the game's own models' lower levels of detail, prebaked by the Editor's "GoF2 > Build > Generated LODs"
// (GeneratedLodsBuilder) into Resources/GoF2Data/generated_lods.bytes: per mesh (LodMeshes.Key) its levels as triangle
// lists over its own vertices (LodMeshes.Write). Read once, when the first object asks; a level's mesh is made the first
// time an object needs it (LodMeshes.Build) and shared by every copy after. No file (not baked yet): no levels, the game
// draws as before.

using System.Collections.Generic;
using System.IO;
using UnityEngine;

namespace GoF2Remake.Visuals
{
    [Unity.Scripting.LifecycleManagement.NoAutoStaticsCleanup]
    public static class GameLods
    {
        public const string ResourcePath = "GoF2Data/generated_lods";
        public const int Magic = 0x31444C47;   // "GLD1"
        public const int Version = 1;

        static byte[] data;
        static Dictionary<string, int> offsets;
        static readonly Dictionary<Mesh, Mesh[]> built = new Dictionary<Mesh, Mesh[]>();

        [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.SubsystemRegistration)]
        static void ResetStatics() { data = null; offsets = null; built.Clear(); }

        static void Load()
        {
            if (offsets != null) return;
            offsets = new Dictionary<string, int>();
            data = GoF2Remake.Data.GameContent.Bytes(ResourcePath);   // (the player's converted copy, else Resources)
            if (data == null) return;   // not baked yet
            try
            {
                using var r = new BinaryReader(new MemoryStream(data, false));
                if (r.ReadInt32() != Magic || r.ReadInt32() != Version) { Debug.LogWarning("GameLods: generated_lods.bytes is another version: run GoF2 > Build > Generated LODs"); data = null; return; }
                int count = r.ReadInt32();
                for (int i = 0; i < count; i++)
                {
                    string key = r.ReadString();
                    int length = r.ReadInt32();
                    offsets[key] = (int)r.BaseStream.Position;
                    r.BaseStream.Position += length;
                }
            }
            catch (System.Exception e) { Debug.LogWarning("GameLods: " + e.Message); offsets.Clear(); data = null; }
        }

        /// <summary>The library has levels for this game mesh.</summary>
        public static bool Has(Mesh mesh)
        {
            if (mesh == null) return false;
            Load();
            return offsets.Count > 0 && offsets.ContainsKey(LodMeshes.Key(mesh));
        }

        /// <summary>The mesh's lower levels (level 1, level 2...), made on first use; empty without an entry.</summary>
        public static Mesh[] Get(Mesh mesh)
        {
            if (mesh == null) return System.Array.Empty<Mesh>();
            if (built.TryGetValue(mesh, out var made)) return made;
            Load();
            made = System.Array.Empty<Mesh>();
            if (data != null && offsets.TryGetValue(LodMeshes.Key(mesh), out int offset))
                try
                {
                    using var r = new BinaryReader(new MemoryStream(data, offset, data.Length - offset, false));
                    var levels = LodMeshes.Read(r);
                    var list = new List<Mesh>();
                    for (int l = 0; l < levels.Length; l++)
                    {
                        var m = LodMeshes.Build(mesh, levels[l], $"{mesh.name}_lod_gen{l + 1}");
                        if (m == null) break;
                        list.Add(m);
                    }
                    made = list.ToArray();
                }
                catch (System.Exception e) { Debug.LogWarning($"GameLods: {mesh.name}: {e.Message}"); }
            built[mesh] = made;
            return made;
        }
    }
}
