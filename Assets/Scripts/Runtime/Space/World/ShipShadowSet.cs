// ShipShadowSet.cs
// Remake: the hangar ships' soft floor shadows (HangarShipShadow), baked by "GoF2 > Build > Hangar Shadows"
// (HangarShadowsBuilder) into Resources/GoF2Station/ShipShadows: per ship assembly its hull seen from above (R the
// underside's height, G the silhouette, A a wide soft halo), the square it covers in the ship's own space (centre x / z and
// side, Unity metres at scale 1), the hull's lowest point and R's range, plus the shadow material (GoF2/HangarShadow, black). The meshes are
// imported non-readable, so a build can't rasterise them itself; the mods' ships (glTFast keeps their meshes readable)
// are baked at run time (ShipShadowBaker), the rest without an entry (the debug capital hulls) get a soft oval of their
// bounds.

using System.Collections.Generic;
using UnityEngine;

namespace GoF2Remake.World
{
    [Unity.Scripting.LifecycleManagement.NoAutoStaticsCleanup]
    public class ShipShadowSet : ScriptableObject
    {
        public const string ResourcePath = "GoF2Station/ShipShadows";

        [System.Serializable]
        public class Entry
        {
            public string assembly;
            public Texture2D texture;
            public Vector2 center;   // the covered square's centre in the ship's space (x, z), m
            public float size;       // its side, m
            public float bottom;     // the hull's lowest point (y), m
            public float heightRange; // R = (underside y - bottom) / heightRange, m
        }

        public Material material;
        public Texture2D oval;
        public List<Entry> entries = new List<Entry>();

        static ShipShadowSet loaded;

        [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.SubsystemRegistration)]
        static void ResetStatics() => loaded = null;
        Dictionary<string, Entry> byName;

        public static ShipShadowSet Load()
        {
            if (loaded == null) loaded = GoF2Remake.Data.OriginalRefs.Load<ShipShadowSet>(ResourcePath);
            return loaded;
        }

        public Entry Find(string assembly)
        {
            if (byName == null)
            {
                byName = new Dictionary<string, Entry>();
                foreach (var e in entries) if (e != null && e.assembly != null) byName[e.assembly] = e;
            }
            return assembly != null && byName.TryGetValue(assembly, out var found) ? found : null;
        }
    }
}
