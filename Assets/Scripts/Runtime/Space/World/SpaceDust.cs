// SpaceDust.cs
// One of the particle sets the original keeps around the camera (Level::initParticleSystems 0xcc990):
//   SET_STARS: 500 white space_particle sprites, 20 + rand(40) units, R 10000, full between 2000 and 5000.
//   SET_FOG:   15 fog.png sprites of 10000 units, tinted per system (x0.6, alpha 0xbb), R 10000, fade in to 1000.
// The GoF2/SpaceDust shader wraps every sprite around the camera and applies the distance fades, so this only
// builds the mesh once (centres in a cube of 2R, corner offsets per vertex).

using UnityEngine;

namespace GoF2Remake.World
{
    [RequireComponent(typeof(MeshFilter), typeof(MeshRenderer))]
    public class SpaceDust : MonoBehaviour
    {
        const float M = OrbitLayout.MetersPerUnit;

        Color full;

        /// <summary>The sprites' strength, 0..1: an arrival from between the systems eases its fog in (Visuals.WarpSky).</summary>
        public void Fade(float amount)
        {
            var mat = GetComponent<MeshRenderer>().sharedMaterial;
            if (mat != null) mat.SetColor("_Color", new Color(full.r, full.g, full.b, full.a * amount));
        }

        public void Build(Material material, int count, float minSizeUnits, float maxSizeUnits, Color color,
                          float radiusUnits, float fadeOutStartUnits, float fadeInEndUnits)
        {
            float r = radiusUnits * M;
            var verts = new Vector3[count * 4];
            var uv = new Vector2[count * 4];
            var corners = new Vector2[count * 4];
            var tris = new int[count * 6];
            for (int i = 0; i < count; i++)
            {
                var centre = new Vector3(Random.Range(-r, r), Random.Range(-r, r), Random.Range(-r, r));
                float h = Random.Range(minSizeUnits, maxSizeUnits) * M * 0.5f;
                for (int k = 0; k < 4; k++)
                {
                    int v = i * 4 + k;
                    verts[v] = centre;
                    float cx = k == 1 || k == 2 ? 1f : -1f, cy = k >= 2 ? 1f : -1f;
                    corners[v] = new Vector2(cx * h, cy * h);
                    uv[v] = new Vector2(cx * 0.5f + 0.5f, cy * 0.5f + 0.5f);
                }
                int t = i * 6, b = i * 4;
                tris[t] = b; tris[t + 1] = b + 2; tris[t + 2] = b + 1;
                tris[t + 3] = b; tris[t + 4] = b + 3; tris[t + 5] = b + 2;
            }
            var mesh = new Mesh { name = name, vertices = verts, uv = uv, triangles = tris };
            mesh.SetUVs(1, corners);
            mesh.bounds = new Bounds(Vector3.zero, Vector3.one * 1e7f);   // positions are rewritten in the shader
            GetComponent<MeshFilter>().sharedMesh = mesh;

            var mr = GetComponent<MeshRenderer>();
            mr.sharedMaterial = material != null ? new Material(material) : null;
            mr.shadowCastingMode = UnityEngine.Rendering.ShadowCastingMode.Off;
            mr.receiveShadows = false;
            if (mr.sharedMaterial == null) return;
            var mat = mr.sharedMaterial;
            full = color;
            mat.SetColor("_Color", color);
            mat.SetFloat("_Radius", r);
            mat.SetFloat("_FadeOutStart", fadeOutStartUnits * M);
            mat.SetFloat("_FadeInEnd", fadeInEndUnits * M);
            mat.SetFloat("_Inner", 0f);
        }
    }
}
