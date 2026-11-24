// ModGltfQuantizedTests.cs
// Feature: docs/notes/mods-models.md "Quantized models": a mod model with KHR_mesh_quantization (the Chorus importer's
// stations: normals and tangents as normalized bytes, vertex colours as normalized unsigned bytes) loads through
// Modding.ModGltf (glTFast) with its normals and colours as written.

using System.Collections;
using System.IO;
using System.Text;
using GoF2Remake.Modding;
using NUnit.Framework;
using UnityEngine;
using UnityEngine.TestTools;

namespace GoF2Remake.Tests
{
    public class ModGltfQuantizedTests : SandboxTest
    {
        ModSource source;

        [TearDown]
        public void CloseSource() { source?.Dispose(); source = null; }

        /// <summary>One triangle: float positions, its normal (0, 0, 1) as BYTE normalized (padded to 4), its colour red as
        /// UNSIGNED_BYTE normalized; KHR_mesh_quantization required.</summary>
        static byte[] QuantizedTriangle()
        {
            var bin = new MemoryStream();
            var w = new BinaryWriter(bin);
            foreach (var v in new[] { 0f, 0f, 0f, 1f, 0f, 0f, 0f, 1f, 0f }) w.Write(v);   // 36 bytes
            for (int i = 0; i < 3; i++) { w.Write((sbyte)0); w.Write((sbyte)0); w.Write((sbyte)127); w.Write((sbyte)0); }   // 12
            for (int i = 0; i < 3; i++) { w.Write((byte)255); w.Write((byte)0); w.Write((byte)0); w.Write((byte)255); }   // 12
            w.Write((ushort)0); w.Write((ushort)1); w.Write((ushort)2); w.Write((ushort)0);   // 6 + 2 padding
            w.Flush();
            var data = bin.ToArray();
            string json = "{\"asset\":{\"version\":\"2.0\"},\"extensionsUsed\":[\"KHR_mesh_quantization\"],\"extensionsRequired\":[\"KHR_mesh_quantization\"]," +
                "\"scene\":0,\"scenes\":[{\"nodes\":[0]}],\"nodes\":[{\"name\":\"q\",\"mesh\":0}]," +
                "\"meshes\":[{\"primitives\":[{\"attributes\":{\"POSITION\":0,\"NORMAL\":1,\"COLOR_0\":2},\"indices\":3}]}]," +
                "\"accessors\":[{\"bufferView\":0,\"componentType\":5126,\"count\":3,\"type\":\"VEC3\",\"min\":[0,0,0],\"max\":[1,1,0]}," +
                "{\"bufferView\":1,\"componentType\":5120,\"normalized\":true,\"count\":3,\"type\":\"VEC3\"}," +
                "{\"bufferView\":2,\"componentType\":5121,\"normalized\":true,\"count\":3,\"type\":\"VEC4\"}," +
                "{\"bufferView\":3,\"componentType\":5123,\"count\":3,\"type\":\"SCALAR\"}]," +
                "\"bufferViews\":[{\"buffer\":0,\"byteOffset\":0,\"byteLength\":36,\"target\":34962}," +
                "{\"buffer\":0,\"byteOffset\":36,\"byteLength\":12,\"byteStride\":4,\"target\":34962}," +
                "{\"buffer\":0,\"byteOffset\":48,\"byteLength\":12,\"target\":34962}," +
                "{\"buffer\":0,\"byteOffset\":60,\"byteLength\":6,\"target\":34963}]," +
                "\"buffers\":[{\"byteLength\":" + data.Length + "}]}";
            var js = Encoding.UTF8.GetBytes(json);
            int jsPad = (4 - js.Length % 4) % 4;
            var glb = new MemoryStream();
            var g = new BinaryWriter(glb);
            g.Write(0x46546C67u); g.Write(2u); g.Write((uint)(12 + 8 + js.Length + jsPad + 8 + data.Length));
            g.Write((uint)(js.Length + jsPad)); g.Write(0x4E4F534Au); g.Write(js); for (int i = 0; i < jsPad; i++) g.Write((byte)' ');
            g.Write((uint)data.Length); g.Write(0x004E4942u); g.Write(data);
            g.Flush();
            return glb.ToArray();
        }

        [UnityTest]
        public IEnumerator AQuantizedModelLoadsWithItsNormalsAndColours()
        {
            string dir = Path.Combine(Sandbox, "quant_test");
            Directory.CreateDirectory(dir);
            File.WriteAllText(Path.Combine(dir, ModManifest.FileName), "{ \"id\": \"quant_test\", \"name\": \"Quantized test\" }");
            source = ModSource.Open(dir);
            var mod = new ModInfo(ModManifest.Read(source), source);
            var load = ModGltf.Load(mod, QuantizedTriangle(), new GLTFast.UninterruptedDeferAgent());
            while (!load.IsCompleted) yield return null;
            var import = load.Result;
            Assert.IsNotNull(import, "glTFast reads KHR_mesh_quantization");
            var root = new GameObject("quantized");
            try
            {
                var shown = ModGltf.InstantiateMain(import, root.transform);
                while (!shown.IsCompleted) yield return null;
                Assert.IsTrue(shown.Result);
                var mf = root.GetComponentInChildren<MeshFilter>();
                Assert.IsNotNull(mf);
                var mesh = mf.sharedMesh;
                Assert.AreEqual(3, mesh.vertexCount);
                // (glTF to Unity mirrors x: the normal's z stays, its length 1)
                Assert.AreEqual(1f, Mathf.Abs(mesh.normals[0].z), 0.01f, "the byte normal");
                Assert.AreEqual(1f, mesh.colors[0].r, 0.01f, "the byte colour's red");
                Assert.AreEqual(0f, mesh.colors[0].g, 0.01f);
            }
            finally { Object.DestroyImmediate(root); import.Dispose(); }
        }
    }
}
