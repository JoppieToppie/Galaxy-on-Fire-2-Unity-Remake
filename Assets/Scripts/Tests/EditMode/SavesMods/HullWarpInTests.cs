// HullWarpInTests.cs
// Feature: Modding/README.md ships.json "warpIn" (Chorus's Omen tissue warping in; World.HullWarpIn): expected, the ship's
// hull-shader renderers start cut away whole and come in over "seconds" (the cut 1 - progress, the edge and colour given),
// shadows off meanwhile and back after; a model added later is picked up; other renderers are left alone.

using GoF2Remake.Data;
using GoF2Remake.World;
using NUnit.Framework;
using UnityEngine;

namespace GoF2Remake.Tests
{
    public class HullWarpInTests
    {
        [Test]
        public void TheShipMaterializesOverItsSeconds()
        {
            var c = JsonUtility.FromJson<CustomShipData>("{ \"id\": \"x\", \"warpIn\": { \"seconds\": 4, \"color\": [6.9, 0, 0] } }");
            Assert.AreEqual(4f, c.warpIn.seconds);
            Assert.AreEqual(0.02f, c.warpIn.edge, 1e-6f, "Chorus's Edge Thickness");
            Assert.IsNull(JsonUtility.FromJson<CustomShipData>("{ \"id\": \"x\" }").warpIn?.color);

            var shader = Shader.Find("GoF2/Hull");
            var mat = new Material(shader);
            var ship = new GameObject("ship");
            try
            {
                var hull = GameObject.CreatePrimitive(PrimitiveType.Cube); hull.transform.SetParent(ship.transform);
                hull.GetComponent<Renderer>().sharedMaterial = mat;
                var plain = GameObject.CreatePrimitive(PrimitiveType.Sphere); plain.transform.SetParent(ship.transform);
                var w = HullWarpIn.Begin(ship, c.warpIn);
                Assert.AreEqual(1, w.Parts, "the hull-shader renderer only");
                var block = new MaterialPropertyBlock();
                hull.GetComponent<Renderer>().GetPropertyBlock(block);
                Assert.AreEqual(1f, block.GetVector("_WreckFx").y, "all cut away at first");
                Assert.AreEqual(0.02f, block.GetVector("_WreckFx").w, 1e-6f);
                Assert.AreEqual(3.45f, block.GetColor("_WreckBurnColor").r, 1e-4f, "half the colour: the shader doubles the edge");
                Assert.AreEqual(UnityEngine.Rendering.ShadowCastingMode.Off, hull.GetComponent<Renderer>().shadowCastingMode);
                var late = GameObject.CreatePrimitive(PrimitiveType.Cube); late.transform.SetParent(ship.transform);
                late.GetComponent<Renderer>().sharedMaterial = mat;
                w.Step(1f);
                Assert.AreEqual(2, w.Parts, "a model loaded later");
                hull.GetComponent<Renderer>().GetPropertyBlock(block);
                Assert.AreEqual(0.75f, block.GetVector("_WreckFx").y, 1e-4f, "a quarter in");
                w.Step(3.5f);
                hull.GetComponent<Renderer>().GetPropertyBlock(block);
                Assert.AreEqual(0f, block.GetVector("_WreckFx").y, "whole");
                Assert.AreEqual(UnityEngine.Rendering.ShadowCastingMode.On, hull.GetComponent<Renderer>().shadowCastingMode, "its shadows back");
                Assert.IsFalse(ship.TryGetComponent<HullWarpIn>(out _), "done");
                plain.GetComponent<Renderer>().GetPropertyBlock(block);
                Assert.IsTrue(block.isEmpty);
            }
            finally { Object.DestroyImmediate(ship); Object.DestroyImmediate(mat); }
        }
    }
}
