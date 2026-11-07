// PlanetMeshes.cs
// Remake mods: the meshes a station's own planet look needs wherever a planet is drawn as a 3D body (the star map's system
// view, UI.StarMap; the planets round a sun between the systems, World.InterstellarField): a UV sphere for a "mapTexture"
// wrapped round it, a quad for a "planetTexture" disc facing the camera. Made once each.

using System.Collections.Generic;
using UnityEngine;

namespace GoF2Remake.Visuals
{
    [Unity.Scripting.LifecycleManagement.NoAutoStaticsCleanup]
    public static class PlanetMeshes
    {
        static readonly Dictionary<float, Mesh> wrappedSpheres = new Dictionary<float, Mesh>();
        static Mesh discQuad;

        [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.SubsystemRegistration)]
        static void ResetStatics() { wrappedSpheres.Clear(); discQuad = null; }

        /// <summary>A UV sphere of 'radius' with longitude on u (0..1 once round the Y axis) and latitude on v (0 at the
        /// bottom pole), for an equirectangular image.</summary>
        public static Mesh WrappedSphere(float radius)
        {
            if (wrappedSpheres.TryGetValue(radius, out var m) && m != null) return m;
            const int seg = 48, rings = 24;
            var v = new List<Vector3>();
            var n = new List<Vector3>();
            var uv = new List<Vector2>();
            for (int y = 0; y <= rings; y++)
            {
                float lat = Mathf.PI * (y / (float)rings - 0.5f);
                for (int x = 0; x <= seg; x++)
                {
                    float lon = 2f * Mathf.PI * x / seg;
                    var dir = new Vector3(Mathf.Cos(lat) * Mathf.Sin(lon), Mathf.Sin(lat), Mathf.Cos(lat) * Mathf.Cos(lon));
                    v.Add(dir * radius);
                    n.Add(dir);
                    uv.Add(new Vector2(x / (float)seg, y / (float)rings));
                }
            }
            var tris = new List<int>();
            for (int y = 0; y < rings; y++)
                for (int x = 0; x < seg; x++)
                {
                    int a = y * (seg + 1) + x, b = a + seg + 1;
                    tris.Add(a); tris.Add(b); tris.Add(a + 1);
                    tris.Add(a + 1); tris.Add(b); tris.Add(b + 1);
                }
            m = new Mesh { name = "map planet (wrapped)" };
            m.SetVertices(v);
            m.SetNormals(n);
            m.SetUVs(0, uv);
            m.SetTriangles(tris, 0);
            m.RecalculateTangents();
            m.RecalculateBounds();
            return wrappedSpheres[radius] = m;
        }
        /// <summary>A unit quad in the XY plane, seen from -Z (FaceDiscs turns its +Z away from the camera).</summary>
        public static Mesh DiscQuad()
        {
            if (discQuad != null) return discQuad;
            discQuad = new Mesh { name = "map planet disc" };
            discQuad.SetVertices(new List<Vector3> { new Vector3(-0.5f, -0.5f, 0f), new Vector3(0.5f, -0.5f, 0f), new Vector3(-0.5f, 0.5f, 0f), new Vector3(0.5f, 0.5f, 0f) });
            discQuad.SetUVs(0, new List<Vector2> { new Vector2(0f, 0f), new Vector2(1f, 0f), new Vector2(0f, 1f), new Vector2(1f, 1f) });
            discQuad.SetColors(new List<Color> { Color.white, Color.white, Color.white, Color.white });
            discQuad.SetTriangles(new[] { 0, 1, 2, 1, 3, 2 }, 0);   // facing -Z
            discQuad.RecalculateNormals();
            discQuad.RecalculateBounds();
            return discQuad;
        }
    }
}
