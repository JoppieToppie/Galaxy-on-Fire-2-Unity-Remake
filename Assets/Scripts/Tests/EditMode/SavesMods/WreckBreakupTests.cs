// WreckBreakupTests.cs
// Feature: docs/notes/mods-models.md "Wrecks that break apart": ships.json "wreck" (CustomShipData.wreck) is read with its
// defaults; World.WreckBreakup flings its model's meshes apart from the wreck's middle with the ship's motion, slows them,
// shrinks them away over the last quarter and is gone at its end.

using GoF2Remake.Data;
using NUnit.Framework;
using UnityEngine;

namespace GoF2Remake.Tests
{
    public class WreckBreakupTests
    {
        [Test]
        public void TheWreckIsReadWithItsDefaults()
        {
            var c = JsonUtility.FromJson<CustomShipData>("{ \"id\": \"x\", \"wreck\": { \"model\": \"wrecks/x.glb\", \"seconds\": 9 } }");
            Assert.AreEqual("wrecks/x.glb", c.wreck.model);
            Assert.AreEqual(9f, c.wreck.seconds);
            Assert.AreEqual(1f, c.wreck.speed);
            Assert.IsNull(JsonUtility.FromJson<CustomShipData>("{ \"id\": \"x\" }").wreck?.model, "none: it explodes and is gone");
        }

        [Test]
        public void ThePiecesFlyApartSlowAndShrinkAway()
        {
            var root = new GameObject("wreck");
            try
            {
                var left = GameObject.CreatePrimitive(PrimitiveType.Cube); left.transform.SetParent(root.transform); left.transform.localPosition = new Vector3(-5f, 0f, 0f);
                var right = GameObject.CreatePrimitive(PrimitiveType.Cube); right.transform.SetParent(root.transform); right.transform.localPosition = new Vector3(5f, 0f, 0f);
                var w = GoF2Remake.World.WreckBreakup.Break(root, new CustomWreck { seconds = 4f, speed = 1f, spin = 0f }, new Vector3(0f, 0f, 10f));
                Assert.AreEqual(2, w.Pieces);
                w.Step(1f);
                var lc = left.GetComponent<Renderer>().bounds.center; var rc = right.GetComponent<Renderer>().bounds.center;
                Assert.Less(lc.x, -5f, "the left piece flies left");
                Assert.Greater(rc.x, 5f, "the right one right");
                Assert.Greater(lc.z, 5f, "both with the ship's motion");
                float gap1 = rc.x - lc.x;
                w.Step(1f);
                float gap2 = right.GetComponent<Renderer>().bounds.center.x - left.GetComponent<Renderer>().bounds.center.x;
                Assert.Less(gap2 - gap1, gap1 - 10f, "slowing down");
                w.Step(1.6f);
                Assert.Less(left.transform.localScale.x, 0.5f, "shrinking over the last quarter");
                w.Step(1f);
                Assert.IsTrue(root == null, "gone at its end");
            }
            finally { if (root != null) Object.DestroyImmediate(root); }
        }

        [Test]
        public void TheHullsPassesCompileWithTheWreckCut()
        {
            // HullWreck.hlsl in the forward pass and around URP's depth fragments, compiled now (a render would)
            var a = Resources.Load<GoF2Remake.Modding.ModAssets>(GoF2Remake.Modding.ModAssets.ResourcePath);
            var m = new Material(a.litOpaque);
            try
            {
                m.SetVector("_WreckFx", new Vector4(1f, 0.5f, 4f, 0.08f));
                m.SetVector("_WreckBurn", new Vector4(1f, 0f, 3f, 3f));   // (and Chorus's mapped burn)
                foreach (var name in new[] { "ForwardLit", "DepthOnly", "DepthNormals" })
                {
                    int pass = m.FindPass(name);
                    Assert.GreaterOrEqual(pass, 0, name);
                    UnityEditor.ShaderUtil.CompilePass(m, pass, true);
                }
                var errors = System.Linq.Enumerable.Where(UnityEditor.ShaderUtil.GetShaderMessages(m.shader),
                    x => x.severity == UnityEditor.Rendering.ShaderCompilerMessageSeverity.Error);
                CollectionAssert.IsEmpty(System.Linq.Enumerable.Select(errors, x => $"{x.file}:{x.line} {x.message}"));
            }
            finally { Object.DestroyImmediate(m); }
        }

        [Test]
        public void HullPiecesBurnThenDisintegrate()
        {
            Assert.AreEqual(1f, GoF2Remake.World.WreckBreakup.BurnAt(0f, 1f));
            Assert.AreEqual(0.5f, GoF2Remake.World.WreckBreakup.BurnAt(0.25f, 1f), 1e-5f, "cooling");
            Assert.AreEqual(0f, GoF2Remake.World.WreckBreakup.BurnAt(0.5f, 1f), "cold by halfway");
            Assert.AreEqual(0f, GoF2Remake.World.WreckBreakup.DissolveAt(0.5f));
            Assert.AreEqual(0.5f, GoF2Remake.World.WreckBreakup.DissolveAt(0.8f), 1e-5f);
            Assert.AreEqual(1f, GoF2Remake.World.WreckBreakup.DissolveAt(1f));
            var c = JsonUtility.FromJson<CustomShipData>("{ \"id\": \"x\", \"wreck\": { \"model\": \"w.glb\" } }");
            Assert.AreEqual(1f, c.wreck.burn);
            Assert.IsTrue(c.wreck.dissolve);

            var shader = Shader.Find("GoF2/Hull");
            Assert.IsNotNull(shader);
            var root = new GameObject("wreck");
            var mat = new Material(shader);
            try
            {
                var hull = GameObject.CreatePrimitive(PrimitiveType.Cube); hull.transform.SetParent(root.transform);
                hull.GetComponent<Renderer>().sharedMaterial = mat;
                var plain = GameObject.CreatePrimitive(PrimitiveType.Cube); plain.transform.SetParent(root.transform); plain.transform.localPosition = new Vector3(0f, 0f, 3f);
                var w = GoF2Remake.World.WreckBreakup.Break(root, new CustomWreck { seconds = 10f, spin = 0f }, Vector3.zero);
                int h = -1, o = -1;
                for (int i = 0; i < 2; i++) if (w.FxOf(i) != Vector4.zero) h = i; else o = i;
                Assert.GreaterOrEqual(h, 0, "the hull-shader piece takes the burn");
                Assert.AreEqual(1f, w.FxOf(h).x, "burning at once");
                Assert.AreEqual(4f, w.FxOf(h).z, 1e-4f, "4 cells across the 1-unit cube");
                w.Step(8f);
                Assert.AreEqual(0f, w.FxOf(h).x);
                Assert.AreEqual(0.5f, w.FxOf(h).y, 1e-4f, "half cut away at 0.8 of its life");
                Assert.AreEqual(Vector3.one, hull.transform.localScale, "it disintegrates instead of shrinking");
                Assert.Less(plain.transform.localScale.x, 1f, "the other shrinks away");
                Assert.AreEqual(UnityEngine.Rendering.ShadowCastingMode.Off, hull.GetComponent<Renderer>().shadowCastingMode);
            }
            finally { if (root != null) Object.DestroyImmediate(root); Object.DestroyImmediate(mat); }
        }

        [Test]
        public void ABurnMapGivesThePiecesChorussBurn()
        {
            var c = JsonUtility.FromJson<CustomShipData>("{ \"id\": \"x\", \"wreck\": { \"model\": \"w.glb\", \"burnMap\": \"maps/burn.png\", " +
                "\"burnTiling\": 2, \"burnVertexRed\": true, \"burnUv1\": true } }");
            Assert.AreEqual("maps/burn.png", c.wreck.burnMap);
            Assert.AreEqual(3f, c.wreck.burnFalloff, "Chorus's Burn_Falloff by default");
            Assert.AreEqual(1500f, c.wreck.burnTemperature);

            var root = new GameObject("wreck");
            var mat = new Material(Shader.Find("GoF2/Hull"));
            var map = new Texture2D(4, 4);
            try
            {
                var hull = GameObject.CreatePrimitive(PrimitiveType.Cube); hull.transform.SetParent(root.transform);
                hull.GetComponent<Renderer>().sharedMaterial = mat;
                var w = GoF2Remake.World.WreckBreakup.Break(root, c.wreck, Vector3.zero, map);
                Assert.AreEqual(new Vector4(2f, 0f, 3f, 3f), w.BurnOf(0), "w: by vertex red (1) + over UV1 (2)");
                var block = new MaterialPropertyBlock();
                hull.GetComponent<Renderer>().GetPropertyBlock(block);
                Assert.AreSame(map, block.GetTexture("_WreckBurnMap"));
                Assert.AreEqual(1500f, block.GetVector("_WreckBurnColor").w, "the temperature");
            }
            finally { Object.DestroyImmediate(root); Object.DestroyImmediate(mat); Object.DestroyImmediate(map); }

            var plainRoot = new GameObject("wreck");
            var plainMat = new Material(Shader.Find("GoF2/Hull"));
            try
            {
                var hull = GameObject.CreatePrimitive(PrimitiveType.Cube); hull.transform.SetParent(plainRoot.transform);
                hull.GetComponent<Renderer>().sharedMaterial = plainMat;
                var w = GoF2Remake.World.WreckBreakup.Break(plainRoot, new CustomWreck { seconds = 4f }, Vector3.zero);
                Assert.AreEqual(0f, w.BurnOf(0).z, "no map: the value-noise burn");
            }
            finally { Object.DestroyImmediate(plainRoot); Object.DestroyImmediate(plainMat); }
        }
    }
}
