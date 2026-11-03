// ModJumpEffect.cs
// Modified (remake mods): a jump drive's own jump effect (Modding.ModJumpFx) in the level: its parts as meshes with the
// mod's texture (additive, the GoF2 effect material), each animated by its keys (position, Rx * Ry * Rz rotation, scale,
// opacity) in the data's Blender frame, turned into Unity's through (x, y, z) -> (x, z, y): the data's +Y (toward the
// viewer) is the root's +Z, its +Z (up) the root's +Y. Billboard parts face the camera (keeping their own spin about it).

using System.Collections.Generic;
using GoF2Remake.Modding;
using UnityEngine;

namespace GoF2Remake.World
{
    public class ModJumpEffect : MonoBehaviour
    {
        const float M = 0.05f;   // metres per game unit
        static readonly int ColorId = Shader.PropertyToID("_Color");

        ModJumpFx.Spec spec;
        float timeMs;
        readonly List<(ModJumpFx.Part part, Transform holder, Transform face, Transform node, Renderer renderer)> nodes = new List<(ModJumpFx.Part, Transform, Transform, Transform, Renderer)>();
        readonly List<Object> owned = new List<Object>();
        MaterialPropertyBlock block;

        public float LengthMs => spec != null ? spec.lengthMs : 0f;

        /// <summary>(Modified: 'texture' in place of the spec's, a failed jump's.) The effect with its root at 'position' / 'rotation' (the root's +Z toward the incoming ship), played once.</summary>
        public static ModJumpEffect Spawn(ModJumpFx.Spec spec, Vector3 position, Quaternion rotation, string texture = null)
        {
            var go = new GameObject("jump fx (" + spec.mod?.Id + ")");
            go.transform.SetPositionAndRotation(position, rotation);
            go.transform.localScale = Vector3.one * (spec.scale * M);
            var fx = go.AddComponent<ModJumpEffect>();
            fx.Build(spec, string.IsNullOrEmpty(texture) ? spec.texture : texture);
            return fx;
        }

        /// <summary>Where the meeting point (0, -meetDepth, 0) of the data is, from the root (world units, the root's frame).</summary>
        public static Vector3 MeetOffset(ModJumpFx.Spec spec) => new Vector3(0f, 0f, -spec.meetDepth) * (spec.scale * M);

        static Vector3 Swap(Vector3 v) => new Vector3(v.x, v.z, v.y);

        /// <summary>A rotation of the data's frame in Unity's: the matrix conjugated by the axis swap (a proper rotation again).</summary>
        static Quaternion Swap(Quaternion blender)
        {
            var m = Matrix4x4.Rotate(blender);
            Vector3 c0 = m.GetColumn(0), c1 = m.GetColumn(1), c2 = m.GetColumn(2);
            // S R S: columns of R permuted (y <-> z) and each column's y and z swapped.
            Vector3 f = Swap(c1), u = Swap(c2);   // new column 2 (z) = S * old column 1 (y); new column 1 (y) = S * old column 2 (z)
            return Quaternion.LookRotation(f, u);
        }

        void Build(ModJumpFx.Spec s, string texturePath)
        {
            spec = s;
            block = new MaterialPropertyBlock();
            Material mat = null;
            var assets = ModAssets.Get();
            var texture = ModMaterials.Texture(s.mod, texturePath, false);
            if (assets != null && assets.fxAdditive != null)
            {
                mat = new Material(assets.fxAdditive) { name = name };
                if (texture != null) { texture.wrapMode = TextureWrapMode.Repeat; mat.SetTexture("_MainTex", texture); }
                mat.SetTextureScale("_MainTex", Vector2.one);
                mat.SetTextureOffset("_MainTex", Vector2.zero);
                mat.SetColor("_Color", Color.white);
                if (mat.HasProperty("_Glow")) mat.SetFloat("_Glow", s.glow);
                owned.Add(mat);
            }
            foreach (var p in s.parts)
            {
                var holder = new GameObject(p.name + " holder").transform;
                holder.SetParent(transform, false);
                if (p.hasBase)
                {
                    holder.localPosition = Swap(p.baseLoc);
                    holder.localRotation = Swap(Quaternion.AngleAxis(p.baseRotY * Mathf.Rad2Deg, Vector3.up));
                    holder.localScale = Vector3.one * p.baseScale;
                }
                var face = new GameObject(p.name + " face").transform;   // turned to the camera for billboards
                face.SetParent(holder, false);
                var node = new GameObject(p.name).transform;
                node.SetParent(face, false);
                var verts = new Vector3[p.verts.Length];
                for (int i = 0; i < verts.Length; i++) verts[i] = Swap(p.verts[i]);
                var mesh = new Mesh { name = p.name };
                mesh.vertices = verts;
                mesh.uv = p.uv;
                var colors = new Color[verts.Length];
                for (int i = 0; i < colors.Length; i++) colors[i] = Color.white;
                mesh.colors = colors;
                mesh.triangles = p.tris;
                mesh.RecalculateBounds();
                owned.Add(mesh);
                node.gameObject.AddComponent<MeshFilter>().sharedMesh = mesh;
                var r = node.gameObject.AddComponent<MeshRenderer>();
                r.sharedMaterial = mat;
                r.shadowCastingMode = UnityEngine.Rendering.ShadowCastingMode.Off;
                r.receiveShadows = false;
                nodes.Add((p, holder, face, node, r));
            }
            Pose();
        }

        void Update()
        {
            timeMs += Time.deltaTime * 1000f;
            Pose();
        }

        void LateUpdate()
        {
            var cam = Camera.main;
            if (cam == null) return;
            foreach (var n in nodes)
                if (n.part.billboard)
                {
                    var to = cam.transform.position - n.face.position;
                    if (to.sqrMagnitude > 1e-6f) n.face.rotation = Quaternion.LookRotation(to, transform.up);
                }
        }

        void Pose()
        {
            foreach (var n in nodes)
            {
                var p = n.part;
                float t = timeMs;
                var loc = new Vector3(p.Eval("posX", t, 0f), p.Eval("posY", t, 0f), p.Eval("posZ", t, 0f));
                var rot = Quaternion.AngleAxis(p.Eval("rotX", t, 0f) * Mathf.Rad2Deg, Vector3.right)
                        * Quaternion.AngleAxis(p.Eval("rotY", t, 0f) * Mathf.Rad2Deg, Vector3.up)
                        * Quaternion.AngleAxis(p.Eval("rotZ", t, 0f) * Mathf.Rad2Deg, Vector3.forward);   // Rx * Ry * Rz (the same
                // matrices as the data's), then conjugated by the axis swap
                var scl = new Vector3(p.Eval("sclX", t, 1f), p.Eval("sclY", t, 1f), p.Eval("sclZ", t, 1f));
                // Modified: a moving billboard (the hyper drive's three light points) moves its camera-facing pivot, so the
                // turn to the camera spins it in place instead of swinging its path round the holder.
                if (p.billboard) { n.face.localPosition = Swap(loc); n.node.localPosition = Vector3.zero; }
                else n.node.localPosition = Swap(loc);
                n.node.localRotation = Swap(rot);
                n.node.localScale = Swap(scl);
                float op = Mathf.Max(0f, p.Eval("extra", t, 1f));
                n.renderer.enabled = op > 0.002f;
                block.SetColor(ColorId, new Color(op, op, op, 1f));
                n.renderer.SetPropertyBlock(block);
            }
        }

        void OnDestroy()
        {
            foreach (var o in owned) if (o != null) Destroy(o);
        }
    }
}
